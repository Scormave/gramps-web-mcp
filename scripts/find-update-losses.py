#!/usr/bin/env python3
"""Find data that gramps-web-mcp update tools dropped before 2.3.1.

Gramps Web replaces the whole object on save. Before 2.3.1 the update_* tools
rebuilt the object from the fields they knew, so any update could drop stored
data the call never mentioned (issue #5): citations, notes and privacy of
attributes, styling and links of note text, LDS ordinances, privacy and
citations of event and child references, place URLs, title and alternate
locations, media checksums, and fields of newer Gramps versions.

Gramps Web keeps each change in its transaction history with the object before
and after it. This script reads that history, lists every value an update
emptied and every list entry it removed, and checks whether the current object
still lacks it: missing, restored, changed since, entry removed (the list entry
that held the value is gone) or object deleted. It only reads: a login and GET
requests, several at a time (--workers). Restoring is up to you.

The history holds edits made in the Gramps Web interface too, and a value that
someone cleared on purpose looks the same as a dropped one, so check each
finding against what the edit was meant to do. --user limits the scan to the
account the MCP server used, and --until to the day you upgraded to 2.3.1.

Usage:
    GRAMPS_API_URL=https://gramps.example.com \\
    GRAMPS_USERNAME=me GRAMPS_PASSWORD=... \\
        python3 scripts/find-update-losses.py [--user NAME] [--since DATE] [--until DATE]
            [--removed] [--json FILE] [--workers N]

GRAMPS_REFRESH_TOKEN can replace GRAMPS_USERNAME and GRAMPS_PASSWORD, as for the
server. The account must be allowed to see private records. Needs Python 3.9 or
later and nothing else.

Exit code: 0 if no dropped value is still missing, 1 if some are, 2 on error.
"""

from __future__ import annotations

import argparse
import json
import os
import sys
import threading
import urllib.error
import urllib.parse
import urllib.request
from concurrent.futures import Executor, ThreadPoolExecutor
from dataclasses import dataclass, field
from datetime import datetime, timedelta
from typing import Any, Callable, Iterator

# Gramps object class -> API collection.
COLLECTIONS = {
    "Person": "people",
    "Family": "families",
    "Event": "events",
    "Place": "places",
    "Source": "sources",
    "Citation": "citations",
    "Repository": "repositories",
    "Note": "notes",
    "Media": "media",
    "Tag": "tags",
}

# Gramps TXNADD, TXNUPD, TXNDEL = 0, 1, 2.
TXN_UPDATE = 1

# Keys the server rewrites on every save.
IGNORED_KEYS = {"_class", "change", "birth_ref_index", "death_ref_index"}

# List key -> collection of the handles it holds (plain handles or the "ref" of each entry).
HANDLE_COLLECTIONS = {
    "citation_list": "citations",
    "note_list": "notes",
    "tag_list": "tags",
    "family_list": "families",
    "parent_family_list": "families",
    "event_ref_list": "events",
    "child_ref_list": "people",
    "person_ref_list": "people",
    "media_list": "media",
    "reporef_list": "repositories",
    "placeref_list": "places",
}

PAGE_SIZE = 100
WORKERS = 8
VALUE_WIDTH = 160


class ApiError(Exception):
    pass


class GrampsApi:
    """Gramps Web API client: logs in with a password or a refresh token and retries once on 401."""

    def __init__(self, url: str, username: str | None, password: str | None, refresh_token: str | None):
        self.base = url.rstrip("/").removesuffix("/api")
        self.username = username
        self.password = password
        self.refresh_token = refresh_token
        self.access_token: str | None = None
        self._login_lock = threading.Lock()

    def _send(self, method: str, path: str, body: Any = None, token: str | None = None) -> tuple[Any, Any]:
        data = None if body is None else json.dumps(body).encode()
        request = urllib.request.Request(self.base + path, data=data, method=method)
        request.add_header("Accept", "application/json")
        if data is not None:
            request.add_header("Content-Type", "application/json")
        if token:
            request.add_header("Authorization", f"Bearer {token}")
        with urllib.request.urlopen(request, timeout=120) as response:
            return json.loads(response.read() or b"null"), response.headers

    def _login(self) -> None:
        try:
            if self.username and self.password:
                body, _ = self._send("POST", "/api/token/", {"username": self.username, "password": self.password})
            else:
                body, _ = self._send("POST", "/api/token/refresh/", token=self.refresh_token)
        except urllib.error.HTTPError as error:
            raise ApiError(f"Login to {self.base} failed with HTTP {error.code}.") from None
        except urllib.error.URLError as error:
            raise ApiError(f"Cannot reach {self.base}: {error.reason}") from None
        self.access_token = body["access_token"]

    def _token(self, expired: str | None = None) -> str:
        """The access token, logging in again when there is none or it is the one that just expired.
        Requests run on several threads, so one of them logs in and the others wait for its token."""
        with self._login_lock:
            if self.access_token is None or self.access_token == expired:
                self._login()
            return self.access_token

    def get(self, path: str, params: dict[str, Any] | None = None) -> tuple[Any, Any]:
        if params:
            path += "?" + urllib.parse.urlencode(params)
        token = self._token()
        for attempt in (1, 2):
            try:
                return self._send("GET", path, token=token)
            except urllib.error.HTTPError as error:
                if error.code == 401 and attempt == 1:
                    token = self._token(expired=token)
                    continue
                if error.code == 403:
                    raise ApiError(f"GET {path}: this account may not read the transaction history "
                                   "(it needs permission to see private records).") from None
                raise
        raise AssertionError("unreachable")

    def get_or_none(self, path: str) -> Any:
        try:
            return self.get(path)[0]
        except urllib.error.HTTPError as error:
            if error.code == 404:
                return None
            raise


# A step from an object to a nested value: ("key", name), ("ref", handle, occurrence),
# ("attr", type, value, occurrence) or ("pos", index).
Step = tuple


@dataclass
class Finding:
    kind: str  # "lost": a value was emptied; "removed": a list entry is gone
    obj_class: str
    handle: str
    gramps_id: str | None
    transaction: int
    timestamp: float
    user: str | None
    description: str | None
    path: list[Step]
    value: Any
    status: str = ""


@dataclass
class Stats:
    transactions: int = 0
    scanned: int = 0
    updates: int = 0
    undo: int = 0
    no_data: int = 0
    objects: set[tuple[str, str]] = field(default_factory=set)


def is_atomic(value: dict) -> bool:
    """Dates and Gramps types are compared whole: a changed one is an edit, not a loss."""
    cls = value.get("_class") or ""
    return cls == "Date" or cls.endswith("Type")


def is_empty(value: Any) -> bool:
    if value is None or value is False or value == "":
        return True
    if isinstance(value, (list, dict)) and not value:
        return True
    if isinstance(value, dict) and value.get("_class") == "Date":
        return not value.get("text") and not value.get("sortval")
    return False


def type_label(value: Any) -> Any:
    if isinstance(value, dict):
        return value.get("string") or value.get("value")
    return value


def entry_key(entry: dict) -> tuple | None:
    """References match by handle and attributes by type and value; other entries have no key."""
    if "ref" in entry:
        return ("ref", entry.get("ref"))
    if entry.get("_class") in ("Attribute", "SrcAttribute"):
        return ("attr", type_label(entry.get("type")), entry.get("value"))
    return None


def match_entries(old: list[dict], new: list[dict]) -> tuple[list[tuple[dict, dict, Step]], list[tuple[dict, Step]]]:
    """Pairs each old entry with the new entry it became; returns the pairs and the old entries left over."""
    used = [False] * len(new)
    occurrences: dict[tuple, int] = {}
    pairs: list[tuple[dict, dict, Step]] = []
    unmatched: list[tuple[dict, Step]] = []
    unkeyed: list[tuple[int, dict]] = []

    for index, entry in enumerate(old):
        key = entry_key(entry)
        if key is None:
            unkeyed.append((index, entry))
            continue
        occurrence = occurrences.get(key, 0)
        occurrences[key] = occurrence + 1
        step = (*key, occurrence)
        match = next((j for j, candidate in enumerate(new) if not used[j] and entry_key(candidate) == key), None)
        if match is None:
            unmatched.append((entry, step))
        else:
            used[match] = True
            pairs.append((entry, new[match], step))

    # Entries without a key: an unchanged copy first, then the next unused entry of the same class.
    leftovers = []
    for index, entry in unkeyed:
        same = next((j for j, candidate in enumerate(new) if not used[j] and candidate == entry), None)
        if same is None:
            leftovers.append((index, entry))
        else:
            used[same] = True
    for index, entry in leftovers:
        match = next((j for j, candidate in enumerate(new)
                      if not used[j] and entry_key(candidate) is None
                      and candidate.get("_class") == entry.get("_class")), None)
        if match is None:
            unmatched.append((entry, ("pos", index)))
        else:
            used[match] = True
            pairs.append((entry, new[match], ("pos", match)))
    return pairs, unmatched


def diff(old: Any, new: Any, path: list[Step], out: list[tuple[str, list[Step], Any]]) -> None:
    """Collects the values of `old` that `new` emptied or removed."""
    if is_empty(old):
        return
    if is_empty(new):
        out.append(("lost", path, old))
        return
    if isinstance(old, dict) and isinstance(new, dict):
        if is_atomic(old):
            return
        for key, value in old.items():
            if key not in IGNORED_KEYS:
                diff(value, new.get(key), path + [("key", key)], out)
    elif isinstance(old, list) and isinstance(new, list):
        if all(isinstance(item, str) for item in old + new):
            remaining = list(new)
            for handle in old:
                if handle in remaining:
                    remaining.remove(handle)
                else:
                    out.append(("removed", path, handle))
        elif all(isinstance(item, dict) for item in old + new):
            pairs, unmatched = match_entries(old, new)
            for old_entry, new_entry, step in pairs:
                diff(old_entry, new_entry, path + [step], out)
            for entry, step in unmatched:
                out.append(("removed", path + [step], entry))
        # Lists of numbers or lists (date values, crop rectangles, text ranges) are compared whole above.


def resolve(obj: Any, path: list[Step]) -> Any:
    """The value at `path` in `obj`, or None when any step is missing."""
    current = obj
    for step in path:
        if step[0] == "key":
            current = current.get(step[1]) if isinstance(current, dict) else None
        elif step[0] == "pos":
            current = current[step[1]] if isinstance(current, list) and step[1] < len(current) else None
        else:
            if not isinstance(current, list):
                return None
            key, occurrence = tuple(step[:-1]), step[-1]
            matches = [entry for entry in current if isinstance(entry, dict) and entry_key(entry) == key]
            current = matches[occurrence] if occurrence < len(matches) else None
        if current is None:
            return None
    return current


def contains(current: Any, old: Any) -> bool:
    if current == old:
        return True
    if isinstance(current, list) and isinstance(old, list):
        return all(item in current for item in old)
    return False


def current_status(finding: Finding, current: Any) -> str:
    if current is None:
        return "object deleted"
    if finding.kind == "removed":
        # A handle, or an entry without a key, is looked for anywhere in the list; a keyed entry by its key.
        if isinstance(finding.value, str) or finding.path[-1][0] == "pos":
            list_path = finding.path if isinstance(finding.value, str) else finding.path[:-1]
            entries = resolve(current, list_path)
            return "restored" if isinstance(entries, list) and finding.value in entries else "missing"
        return "missing" if resolve(current, finding.path) is None else "restored"
    value = resolve(current, finding.path)
    if is_empty(value):
        # The list entry that held the value may be gone since; then there is nothing to put it back on.
        parents = [finding.path[:i + 1] for i, step in enumerate(finding.path[:-1]) if step[0] != "key"]
        return "entry removed" if any(resolve(current, parent) is None for parent in parents) else "missing"
    return "restored" if contains(value, finding.value) else "changed since"


def user_name(transaction: dict) -> str | None:
    user = (transaction.get("connection") or {}).get("user")
    if isinstance(user, dict):
        return user.get("name")
    return user


def list_transactions(api: GrampsApi) -> Iterator[dict]:
    page = 1
    while True:
        items, headers = api.get("/api/transactions/history/", {"page": page, "pagesize": PAGE_SIZE})
        yield from items
        total = int(headers.get("X-Total-Count") or 0)
        if len(items) < PAGE_SIZE or (total and page * PAGE_SIZE >= total):
            return
        if page % 10 == 0:
            print(f"... {page * PAGE_SIZE} of {total or '?'} transactions listed", file=sys.stderr)
        page += 1


def is_object_update(change: dict) -> bool:
    return change.get("trans_type") == TXN_UPDATE and change.get("obj_class") in COLLECTIONS


def parallel(executor: Executor, function: Callable[[Any], Any], items: list) -> Iterator[tuple[Any, Any]]:
    """Each item with function(item), in order; the calls run on the executor's threads."""
    return zip(items, executor.map(function, items))


def select(api: GrampsApi, args: argparse.Namespace, stats: Stats) -> list[dict]:
    """The listed transactions to read: in range, by the user, not undone, with updates of objects."""
    selected = []
    for summary in list_transactions(api):
        stats.transactions += 1
        timestamp = summary.get("timestamp") or 0
        if args.since and timestamp < args.since.timestamp():
            continue
        if args.until and timestamp >= (args.until + timedelta(days=1)).timestamp():
            continue
        if args.user and user_name(summary) != args.user:
            continue
        if not any(is_object_update(change) for change in summary.get("changes") or []):
            continue
        if summary.get("undo"):
            stats.undo += 1
            continue
        selected.append(summary)
    return selected


def scan(api: GrampsApi, args: argparse.Namespace, stats: Stats, executor: Executor) -> list[Finding]:
    selected = select(api, args, stats)
    print(f"Reading {len(selected)} transactions with updates ...", file=sys.stderr)
    findings: list[Finding] = []
    read = lambda summary: api.get(f"/api/transactions/history/{summary['id']}", {"old": 1, "new": 1})[0]
    for summary, transaction in parallel(executor, read, selected):
        stats.scanned += 1
        timestamp = summary.get("timestamp") or 0
        for change in transaction.get("changes") or []:
            if not is_object_update(change):
                continue
            old, new = change.get("old_data"), change.get("new_data")
            if not isinstance(old, dict) or not isinstance(new, dict) or not old or not new:
                stats.no_data += 1
                continue
            stats.updates += 1
            stats.objects.add((change["obj_class"], change["obj_handle"]))
            dropped: list[tuple[str, list[Step], Any]] = []
            diff(old, new, [], dropped)
            for kind, path, value in dropped:
                findings.append(Finding(
                    kind=kind,
                    obj_class=change["obj_class"],
                    handle=change["obj_handle"],
                    gramps_id=new.get("gramps_id") or old.get("gramps_id"),
                    transaction=transaction["id"],
                    timestamp=timestamp,
                    user=user_name(transaction),
                    description=transaction.get("description"),
                    path=path,
                    value=value,
                ))
        if stats.scanned % 100 == 0:
            print(f"... {stats.scanned} of {len(selected)} read", file=sys.stderr)
    return findings


class Labels:
    """Shows handles as Gramps IDs, looked up once each."""

    def __init__(self, api: GrampsApi):
        self.api = api
        self.cache: dict[tuple[str, str], str] = {}
        self.wanted: set[tuple[str, str]] | None = None

    def prefetch(self, findings: list[Finding], executor: Executor) -> None:
        """Looks up every handle the findings show, several at a time."""
        self.wanted = set()
        for finding in findings:
            self.path(finding.path)
            self.value(finding)
        keys, self.wanted = sorted(self.wanted), None
        look_up = lambda key: self.api.get_or_none(f"/api/{key[0]}/{key[1]}")
        for key, obj in parallel(executor, look_up, keys):
            self.cache[key] = self.label(key[1], obj)

    @staticmethod
    def label(handle: str, obj: Any) -> str:
        return (obj.get("gramps_id") or obj.get("name") or handle) if obj else f"{handle} (deleted)"

    def handle(self, collection: str | None, handle: Any) -> str:
        if collection is None or not isinstance(handle, str):
            return json.dumps(handle, ensure_ascii=False)
        key = (collection, handle)
        if key not in self.cache:
            if self.wanted is not None:
                self.wanted.add(key)
                return handle
            self.cache[key] = self.label(handle, self.api.get_or_none(f"/api/{collection}/{handle}"))
        return self.cache[key]

    def path(self, path: list[Step]) -> str:
        text = ""
        list_key: str | None = None
        for step in path:
            if step[0] == "key":
                text += ("." if text else "") + step[1]
                list_key = step[1]
            elif step[0] == "ref":
                text += f"[{self.handle(HANDLE_COLLECTIONS.get(list_key or ''), step[1])}]"
            elif step[0] == "attr":
                text += f"[{step[1]}: {step[2]}]"
            else:
                text += f"[{step[1]}]"
        return text or "(object)"

    def value(self, finding: Finding) -> str:
        list_key = next((step[1] for step in reversed(finding.path) if step[0] == "key"), None)
        collection = HANDLE_COLLECTIONS.get(list_key or "")
        value = finding.value
        if collection and isinstance(value, dict) and "ref" in value:
            value = value["ref"]
        if collection and isinstance(value, str):
            text = self.handle(collection, value)
        elif collection and isinstance(value, list) and all(isinstance(item, str) for item in value):
            text = "[" + ", ".join(self.handle(collection, item) for item in value) + "]"
        else:
            text = json.dumps(value, ensure_ascii=False)
        return text if len(text) <= VALUE_WIDTH else text[:VALUE_WIDTH - 1] + "…"


def print_report(findings: list[Finding], stats: Stats, labels: Labels, show_removed: bool) -> None:
    print(f"Read {stats.transactions} transactions; {stats.scanned} in range had updates: "
          f"{stats.updates} updates of {len(stats.objects)} objects.")
    if stats.undo or stats.no_data:
        print(f"Skipped {stats.undo} undo transactions and {stats.no_data} changes without stored data.")

    sections = [("lost", "VALUES AN UPDATE EMPTIED")]
    if show_removed:
        sections.append(("removed", "LIST ENTRIES AN UPDATE REMOVED (often on purpose: replace mode, deleted links)"))
    for kind, title in sections:
        selected = [finding for finding in findings if finding.kind == kind]
        print(f"\n{title}: {len(selected)}")
        last_object = last_transaction = None
        for finding in sorted(selected, key=lambda f: (f.obj_class, f.gramps_id or "", f.transaction)):
            obj = (finding.obj_class, finding.handle)
            if obj != last_object:
                print(f"\n{finding.obj_class} {finding.gramps_id or '?'} ({finding.handle})")
                last_object, last_transaction = obj, None
            if finding.transaction != last_transaction:
                when = datetime.fromtimestamp(finding.timestamp).strftime("%Y-%m-%d %H:%M")
                print(f"  transaction {finding.transaction}, {when}, {finding.user or 'unknown user'}: "
                      f"{finding.description or ''}")
                last_transaction = finding.transaction
            print(f"    {finding.status:<14} {labels.path(finding.path)} = {labels.value(finding)}")

    lost = [finding for finding in findings if finding.kind == "lost"]
    missing = sum(finding.status == "missing" for finding in lost)
    removed = sum(finding.kind == "removed" for finding in findings)
    print(f"\n{len(lost)} emptied values in {len({(f.obj_class, f.handle) for f in lost})} objects, "
          f"{missing} still missing.")
    if not show_removed and removed:
        print(f"{removed} removed list entries are not shown; add --removed to list them.")


def write_json(path: str, findings: list[Finding], labels: Labels) -> None:
    rows = [{
        "kind": finding.kind,
        "status": finding.status,
        "class": finding.obj_class,
        "handle": finding.handle,
        "gramps_id": finding.gramps_id,
        "transaction": finding.transaction,
        "time": datetime.fromtimestamp(finding.timestamp).isoformat(timespec="seconds"),
        "user": finding.user,
        "description": finding.description,
        "path": labels.path(finding.path),
        "steps": [list(step) for step in finding.path],
        "value": finding.value,
    } for finding in findings]
    with open(path, "w", encoding="utf-8") as file:
        json.dump(rows, file, ensure_ascii=False, indent=2)
    print(f"Wrote {len(rows)} findings to {path}.")


def parse_date(text: str) -> datetime:
    try:
        return datetime.strptime(text, "%Y-%m-%d")
    except ValueError:
        raise argparse.ArgumentTypeError(f"expected a date as YYYY-MM-DD, got {text!r}") from None


def main() -> int:
    parser = argparse.ArgumentParser(
        description="List data that gramps-web-mcp update tools before 2.3.1 dropped from a Gramps Web tree. "
                    "Reads only; connection settings come from GRAMPS_API_URL and GRAMPS_USERNAME with "
                    "GRAMPS_PASSWORD, or GRAMPS_REFRESH_TOKEN.")
    parser.add_argument("--user", help="only transactions by this Gramps Web user name (the MCP server's account)")
    parser.add_argument("--since", type=parse_date, help="only transactions on or after this date (YYYY-MM-DD)")
    parser.add_argument("--until", type=parse_date,
                        help="only transactions on or before this date, e.g. the day you upgraded to 2.3.1 (YYYY-MM-DD)")
    parser.add_argument("--removed", action="store_true",
                        help="also list entries an update removed from a list, which is often intended")
    parser.add_argument("--json", metavar="FILE", help="also write every finding, with full values, to FILE")
    parser.add_argument("--workers", type=int, default=WORKERS, metavar="N",
                        help=f"requests to run at once (default {WORKERS}); 1 reads one at a time")
    args = parser.parse_args()
    if args.workers < 1:
        parser.error("--workers must be 1 or more")

    url = os.environ.get("GRAMPS_API_URL")
    username, password = os.environ.get("GRAMPS_USERNAME"), os.environ.get("GRAMPS_PASSWORD")
    refresh_token = os.environ.get("GRAMPS_REFRESH_TOKEN")
    if not url or not ((username and password) or refresh_token):
        parser.error("set GRAMPS_API_URL and either GRAMPS_USERNAME with GRAMPS_PASSWORD or GRAMPS_REFRESH_TOKEN")

    api = GrampsApi(url, username, password, refresh_token)
    stats = Stats()
    executor = ThreadPoolExecutor(max_workers=args.workers)
    try:
        findings = scan(api, args, stats, executor)
        objects = list(dict.fromkeys((finding.obj_class, finding.handle) for finding in findings))
        print(f"Checking {len(objects)} objects as they are now ...", file=sys.stderr)
        load = lambda obj: api.get_or_none(f"/api/{COLLECTIONS[obj[0]]}/{obj[1]}")
        current = dict(parallel(executor, load, objects))
        for finding in findings:
            finding.status = current_status(finding, current[(finding.obj_class, finding.handle)])
        labels = Labels(api)
        labels.prefetch(findings, executor)
        print_report(findings, stats, labels, args.removed)
        if args.json:
            write_json(args.json, findings, labels)
    except ApiError as error:
        print(f"Error: {error}", file=sys.stderr)
        return 2
    except urllib.error.HTTPError as error:
        print(f"Error: HTTP {error.code} for {error.url}", file=sys.stderr)
        return 2
    except urllib.error.URLError as error:
        print(f"Error: {error.reason}", file=sys.stderr)
        return 2
    finally:
        executor.shutdown(wait=False, cancel_futures=True)
    return 1 if any(f.kind == "lost" and f.status == "missing" for f in findings) else 0


if __name__ == "__main__":
    sys.exit(main())
