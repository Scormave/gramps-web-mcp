# The Gramps Web image the integration tests run against. Nothing builds this
# file: GrampsWebImage reads the image from the FROM line, and Dependabot
# proposes new versions of it. GRAMPS_WEB_IMAGE overrides it for one run.
FROM ghcr.io/gramps-project/grampsweb:26.10.1
