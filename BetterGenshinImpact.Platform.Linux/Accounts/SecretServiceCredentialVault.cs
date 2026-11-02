using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BetterGenshinImpact.Pulonia.Services;
using Tmds.DBus.Protocol;

namespace BetterGenshinImpact.Platform.Linux.Accounts;

/// <summary>
/// 用 freedesktop Secret Service（D-Bus <c>org.freedesktop.secrets</c>，GNOME Keyring / KWallet / KeePassXC 都提供）保存账户登录凭据。
/// 条目放在默认集合（通常是“登录”密钥环），属性 <c>application=BetterGI</c>、<c>bgi-target=&lt;目标名&gt;</c>；
/// 秘密值是 {"user","password"} JSON，经 dh-ietf1024-sha256-aes128-cbc-pkcs7 会话加密传输（服务不支持时用 plain 会话，仍只经本机会话总线）。
/// 没有可用的密钥环时抛出带安装说明的异常，绝不退回明文文件。
/// </summary>
public sealed class SecretServiceCredentialVault : IPuloniaCredentialVault
{
    private const string Bus = "org.freedesktop.secrets";
    private const string ServicePath = "/org/freedesktop/secrets";
    private const string ServiceIface = "org.freedesktop.Secret.Service";
    private const string Application = "BetterGI";

    /// <summary>没有密钥环时给用户看的说明</summary>
    public const string NoKeyringHint =
        "没有可用的系统密钥环（freedesktop Secret Service）：请安装并启动 gnome-keyring（或 KWallet、KeePassXC 并启用 Secret Service 集成）后重试。" +
        "BetterGI 不会把账户密码以明文保存在文件里";

    /// <summary>等待用户在密钥环解锁 / 授权对话框中操作的时间</summary>
    public TimeSpan PromptTimeout { get; init; } = TimeSpan.FromMinutes(2);

    /// <inheritdoc />
    public void Write(string target, string userName, string password)
    {
        var secret = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, string> { ["user"] = userName, ["password"] = password });
        try
        {
            Run(async s =>
            {
                var collection = await s.DefaultCollectionAsync();
                await s.UnlockAsync([collection]);
                await s.CreateItemAsync(collection, $"BetterGI 原神账户（{target}）", Attributes(target), secret);
                return true;
            });
        }
        finally { CryptographicOperations.ZeroMemory(secret); }
    }

    /// <inheritdoc />
    public PuloniaCredential? Read(string target) => Run(async s =>
    {
        var items = await s.SearchAsync(Attributes(target));
        if (items.Length == 0) return null;
        await s.UnlockAsync(items[..1]);
        var bytes = await s.GetSecretAsync(items[0]);
        try
        {
            var map = JsonSerializer.Deserialize<Dictionary<string, string>>(bytes)
                      ?? throw new InvalidOperationException("密钥环里的 BetterGI 凭据格式不正确");
            return new PuloniaCredential(map.GetValueOrDefault("user") ?? "", map.GetValueOrDefault("password") ?? "");
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    });

    /// <inheritdoc />
    public void Delete(string target)
    {
        try
        {
            Run(async s =>
            {
                foreach (var item in await s.SearchAsync(Attributes(target)))
                {
                    await s.UnlockAsync([item]);
                    await s.DeleteItemAsync(item);
                }
                return true;
            });
        }
        catch (Exception e) when (e is InvalidOperationException or DBusErrorReplyException or DBusConnectionException or TimeoutException)
        {
            // 与接口约定一致：尽力删除，失败不抛出
        }
    }

    /// <summary>Secret Service 是否可用（诊断用）：返回 null 表示可用，否则为原因</summary>
    public static string? Probe()
    {
        try
        {
            new SecretServiceCredentialVault().Run(async s => { await s.DefaultCollectionAsync(); return true; });
            return null;
        }
        catch (Exception e) when (e is InvalidOperationException or DBusErrorReplyException or DBusConnectionException or TimeoutException)
        {
            return e.Message;
        }
    }

    private static Dictionary<string, string> Attributes(string target) => new()
    {
        ["application"] = Application,
        ["bgi-target"] = target,
    };

    /// <summary>在线程池上同步执行（接口是同步的；避免在界面线程的同步上下文里死锁）</summary>
    private T Run<T>(Func<Session, Task<T>> body)
    {
        return Task.Run(async () =>
        {
            var address = DBusAddress.Session ?? throw new InvalidOperationException(NoKeyringHint + "（没有 D-Bus 会话总线）");
            using var conn = new DBusConnection(address);
            try { await conn.ConnectAsync(); }
            catch (Exception e) when (e is not OutOfMemoryException) { throw new InvalidOperationException(NoKeyringHint + "（连接会话总线失败：" + e.Message + "）", e); }
            var session = new Session(conn, PromptTimeout);
            try
            {
                await session.OpenAsync();
                return await body(session);
            }
            catch (DBusErrorReplyException e) when (e.ErrorName is "org.freedesktop.DBus.Error.ServiceUnknown" or "org.freedesktop.DBus.Error.NameHasNoOwner"
                                              or "org.freedesktop.DBus.Error.Spawn.ServiceNotFound" or "org.freedesktop.DBus.Error.Spawn.ChildExited")
            {
                throw new InvalidOperationException(NoKeyringHint, e);
            }
            finally { await session.CloseAsync(); }
        }).GetAwaiter().GetResult();
    }

    /// <summary>一次连接上的 Secret Service 会话</summary>
    private sealed class Session(DBusConnection conn, TimeSpan promptTimeout)
    {
        // RFC 2409 第二组（1024 位 MODP），生成元 2：Secret Service 规范的 dh-ietf1024-sha256-aes128-cbc-pkcs7
        private static readonly BigInteger Prime = BigInteger.Parse(
            "00FFFFFFFFFFFFFFFFC90FDAA22168C234C4C6628B80DC1CD129024E088A67CC74020BBEA63B139B22514A08798E3404DDEF9519B3CD3A431B302B0A6DF25F14374FE1356D6D51C245E485B576625E7EC6F44C42E9A637ED6B0BFF5CB6F406B7EDEE386BFB5A899FA5AE9F24117C4B1FE649286651ECE65381FFFFFFFFFFFFFFFF",
            System.Globalization.NumberStyles.HexNumber);

        private string _path = "/";
        private byte[]? _key;

        public async Task OpenAsync()
        {
            var priv = new byte[128];
            RandomNumberGenerator.Fill(priv);
            var x = new BigInteger(priv, isUnsigned: true, isBigEndian: true);
            CryptographicOperations.ZeroMemory(priv);
            var pub = BigInteger.ModPow(2, x, Prime).ToByteArray(isUnsigned: true, isBigEndian: true);
            try
            {
                var (output, path) = await OpenSessionAsync("dh-ietf1024-sha256-aes128-cbc-pkcs7", VariantValue.Array(pub));
                var serverPub = new BigInteger(output.GetArray<byte>(), isUnsigned: true, isBigEndian: true);
                var shared = BigInteger.ModPow(serverPub, x, Prime).ToByteArray(isUnsigned: true, isBigEndian: true);
                var padded = new byte[128];
                shared.CopyTo(padded, 128 - shared.Length);
                _key = HKDF.DeriveKey(HashAlgorithmName.SHA256, padded, 16);
                CryptographicOperations.ZeroMemory(padded);
                CryptographicOperations.ZeroMemory(shared);
                _path = path;
            }
            catch (DBusErrorReplyException e) when (e.ErrorName == "org.freedesktop.DBus.Error.NotSupported")
            {
                (_, _path) = await OpenSessionAsync("plain", VariantValue.String(""));
            }
        }

        private async Task<(VariantValue Output, string Path)> OpenSessionAsync(string algorithm, VariantValue input)
        {
            var w = conn.GetMessageWriter();
            w.WriteMethodCallHeader(Bus, ServicePath, ServiceIface, "OpenSession", "sv", MessageFlags.None);
            w.WriteString(algorithm);
            w.WriteVariant(input);
            return await conn.CallMethodAsync(w.CreateMessage(), static (Message m, object? _) =>
            {
                var r = m.GetBodyReader();
                return (r.ReadVariantValue(), r.ReadObjectPathAsString());
            }, null);
        }

        public async Task CloseAsync()
        {
            if (_key is not null) CryptographicOperations.ZeroMemory(_key);
            if (_path == "/") return;
            try
            {
                var w = conn.GetMessageWriter();
                w.WriteMethodCallHeader(Bus, _path, "org.freedesktop.Secret.Session", "Close", null, MessageFlags.None);
                await conn.CallMethodAsync(w.CreateMessage());
            }
            catch (Exception e) when (e is DBusErrorReplyException or ObjectDisposedException or DBusConnectionException)
            {
            }
        }

        /// <summary>默认集合（别名 default）；没有时尝试“login”集合</summary>
        public async Task<string> DefaultCollectionAsync()
        {
            foreach (var alias in new[] { "default", "login" })
            {
                var w = conn.GetMessageWriter();
                w.WriteMethodCallHeader(Bus, ServicePath, ServiceIface, "ReadAlias", "s", MessageFlags.None);
                w.WriteString(alias);
                var path = await conn.CallMethodAsync(w.CreateMessage(), static (Message m, object? _) => m.GetBodyReader().ReadObjectPathAsString(), null);
                if (path != "/") return path;
            }

            throw new InvalidOperationException("密钥环里没有默认集合（例如“登录”密钥环）：请在密钥环管理器（Seahorse / KWallet / KeePassXC）里创建并设为默认后重试");
        }

        public async Task<string[]> SearchAsync(Dictionary<string, string> attributes)
        {
            var w = conn.GetMessageWriter();
            w.WriteMethodCallHeader(Bus, ServicePath, ServiceIface, "SearchItems", "a{ss}", MessageFlags.None);
            WriteStringMap(ref w, attributes);
            var (unlocked, locked) = await conn.CallMethodAsync(w.CreateMessage(), static (Message m, object? _) =>
            {
                var r = m.GetBodyReader();
                return (r.ReadArrayOfObjectPath(), r.ReadArrayOfObjectPath());
            }, null);
            return unlocked.Select(p => p.ToString()).Concat(locked.Select(p => p.ToString())).ToArray();
        }

        /// <summary>解锁（已解锁时立即返回；锁定时由密钥环弹出解锁对话框）</summary>
        public async Task UnlockAsync(string[] objects)
        {
            var w = conn.GetMessageWriter();
            w.WriteMethodCallHeader(Bus, ServicePath, ServiceIface, "Unlock", "ao", MessageFlags.None);
            WriteObjectPaths(ref w, objects);
            var (_, prompt) = await conn.CallMethodAsync(w.CreateMessage(), static (Message m, object? _) =>
            {
                var r = m.GetBodyReader();
                return (r.ReadArrayOfObjectPath(), r.ReadObjectPathAsString());
            }, null);
            if (prompt != "/" && await PromptAsync(prompt))
                throw new InvalidOperationException("密钥环仍是锁定状态（解锁对话框被取消），无法读写账户密码");
        }

        public async Task CreateItemAsync(string collection, string label, Dictionary<string, string> attributes, byte[] secret)
        {
            var (parameters, value) = Encrypt(secret);
            var w = conn.GetMessageWriter();
            w.WriteMethodCallHeader(Bus, collection, "org.freedesktop.Secret.Collection", "CreateItem", "a{sv}(oayays)b", MessageFlags.None);
            var dict = w.WriteDictionaryStart();
            w.WriteDictionaryEntryStart();
            w.WriteString("org.freedesktop.Secret.Item.Label");
            w.WriteVariant(VariantValue.String(label));
            w.WriteDictionaryEntryStart();
            w.WriteString("org.freedesktop.Secret.Item.Attributes");
            w.WriteSignature("a{ss}");
            WriteStringMap(ref w, attributes);
            w.WriteDictionaryEnd(dict);
            WriteSecret(ref w, parameters, value);
            w.WriteBool(true); // 同属性的条目覆盖（改密码）
            var (_, prompt) = await conn.CallMethodAsync(w.CreateMessage(), static (Message m, object? _) =>
            {
                var r = m.GetBodyReader();
                return (r.ReadObjectPathAsString(), r.ReadObjectPathAsString());
            }, null);
            CryptographicOperations.ZeroMemory(value);
            if (prompt != "/" && await PromptAsync(prompt))
                throw new InvalidOperationException("保存到密钥环被取消");
        }

        public async Task<byte[]> GetSecretAsync(string item)
        {
            var w = conn.GetMessageWriter();
            w.WriteMethodCallHeader(Bus, item, "org.freedesktop.Secret.Item", "GetSecret", "o", MessageFlags.None);
            w.WriteObjectPath(_path);
            var (parameters, value) = await conn.CallMethodAsync(w.CreateMessage(), static (Message m, object? _) =>
            {
                var r = m.GetBodyReader();
                r.AlignStruct();
                r.ReadObjectPathAsString();
                var p = r.ReadArrayOfByte();
                var v = r.ReadArrayOfByte();
                r.ReadString();
                return (p, v);
            }, null);
            if (_key is null) return value;
            try { return Decrypt(parameters, value); }
            finally { CryptographicOperations.ZeroMemory(value); }
        }

        public async Task DeleteItemAsync(string item)
        {
            var w = conn.GetMessageWriter();
            w.WriteMethodCallHeader(Bus, item, "org.freedesktop.Secret.Item", "Delete", null, MessageFlags.None);
            var prompt = await conn.CallMethodAsync(w.CreateMessage(), static (Message m, object? _) => m.GetBodyReader().ReadObjectPathAsString(), null);
            if (prompt != "/") await PromptAsync(prompt);
        }

        /// <summary>执行密钥环的提示（解锁 / 确认对话框），返回是否被取消</summary>
        private async Task<bool> PromptAsync(string prompt)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
#pragma warning disable CS0618 // 新重载需要 SynchronizationContext 参数，行为相同
            using var watch = await conn.WatchSignalAsync(Bus, prompt, "org.freedesktop.Secret.Prompt", "Completed",
                static (Message m, object? _) => m.GetBodyReader().ReadBool(),
                (Exception? e, bool dismissed) =>
                {
                    if (e is not null) tcs.TrySetException(e);
                    else tcs.TrySetResult(dismissed);
                }, null, false, ObserverFlags.None);
#pragma warning restore CS0618
            var w = conn.GetMessageWriter();
            w.WriteMethodCallHeader(Bus, prompt, "org.freedesktop.Secret.Prompt", "Prompt", "s", MessageFlags.None);
            w.WriteString("");
            await conn.CallMethodAsync(w.CreateMessage());
            var done = await Task.WhenAny(tcs.Task, Task.Delay(promptTimeout));
            if (done != tcs.Task) throw new TimeoutException("等待密钥环对话框超时（请在弹出的解锁 / 授权对话框里确认）");
            return await tcs.Task;
        }

        private (byte[] Parameters, byte[] Value) Encrypt(byte[] secret)
        {
            if (_key is null) return ([], (byte[])secret.Clone());
            using var aes = Aes.Create();
            aes.Key = _key;
            var iv = RandomNumberGenerator.GetBytes(16);
            return (iv, aes.EncryptCbc(secret, iv, PaddingMode.PKCS7));
        }

        private byte[] Decrypt(byte[] iv, byte[] value)
        {
            using var aes = Aes.Create();
            aes.Key = _key!;
            return aes.DecryptCbc(value, iv, PaddingMode.PKCS7);
        }

        private void WriteSecret(ref MessageWriter w, byte[] parameters, byte[] value)
        {
            w.WriteStructureStart();
            w.WriteObjectPath(_path);
            w.WriteArray(parameters);
            w.WriteArray(value);
            w.WriteString("text/plain; charset=utf8");
        }

        private static void WriteStringMap(ref MessageWriter w, Dictionary<string, string> map)
        {
            var start = w.WriteDictionaryStart();
            foreach (var (k, v) in map)
            {
                w.WriteDictionaryEntryStart();
                w.WriteString(k);
                w.WriteString(v);
            }
            w.WriteDictionaryEnd(start);
        }

        private static void WriteObjectPaths(ref MessageWriter w, string[] paths)
        {
            var start = w.WriteArrayStart(DBusType.ObjectPath);
            foreach (var p in paths) w.WriteObjectPath(p);
            w.WriteArrayEnd(start);
        }
    }
}
