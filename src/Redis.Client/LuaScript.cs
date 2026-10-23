using System.Security.Cryptography;
using System.Text;

namespace Redis.Client;

/// <summary>
/// A Lua script with its precomputed SHA1 digest. Create one instance per script and reuse it; the client runs it
/// with <c>EVALSHA</c> and sends the source only when the server does not have it cached.
/// </summary>
public sealed class LuaScript
{
    /// <summary>Initializes a new instance of the <see cref="LuaScript"/> class.</summary>
    /// <param name="source">The Lua source code.</param>
    public LuaScript(string source)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);
        Source = source;
        Sha1 = Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(source)));
    }

    /// <summary>Gets the Lua source code.</summary>
    public string Source { get; }

    /// <summary>Gets the lowercase hex SHA1 digest used by <c>EVALSHA</c>.</summary>
    public string Sha1 { get; }

    /// <summary>Returns the digest.</summary>
    /// <returns>The SHA1 digest.</returns>
    public override string ToString() => Sha1;
}
