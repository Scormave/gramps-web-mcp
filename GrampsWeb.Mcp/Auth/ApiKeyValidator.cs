using System.Security.Cryptography;
using System.Text;
using GrampsWeb.Mcp.Config;

namespace GrampsWeb.Mcp.Auth;

internal sealed class ApiKeyValidator
{
    private readonly byte[][] _keyHashes;

    public ApiKeyValidator(McpAuthConfig config)
    {
        _keyHashes = config.ApiKeys
            .Select(key => SHA256.HashData(Encoding.UTF8.GetBytes(key)))
            .ToArray();
    }

    public bool IsValid(string presented)
    {
        if (string.IsNullOrEmpty(presented))
        {
            return false;
        }

        var presentedHash = SHA256.HashData(Encoding.UTF8.GetBytes(presented));
        foreach (var keyHash in _keyHashes)
        {
            if (CryptographicOperations.FixedTimeEquals(presentedHash, keyHash))
            {
                return true;
            }
        }

        return false;
    }
}
