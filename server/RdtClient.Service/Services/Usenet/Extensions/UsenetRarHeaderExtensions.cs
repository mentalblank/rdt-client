using System.Security.Cryptography;
using System.Text;
using RdtClient.Data.Models.Data;
using SharpCompress.Common.Rar.Headers;

namespace RdtClient.Service.Services.Usenet.Extensions;

public static class UsenetRarHeaderExtensions
{
    public static Byte GetCompressionMethod(this IRarHeader header)
    {
        return (Byte)header.GetReflectionProperty("CompressionMethod")!;
    }

    public static Int64 GetDataStartPosition(this IRarHeader header)
    {
        return (Int64)header.GetReflectionProperty("DataStartPosition")!;
    }

    public static Int64 GetAdditionalDataSize(this IRarHeader header)
    {
        return (Int64)header.GetReflectionProperty("AdditionalDataSize")!;
    }

    public static Int64 GetCompressedSize(this IRarHeader header)
    {
        return (Int64)header.GetReflectionProperty("CompressedSize")!;
    }

    public static Int64 GetUncompressedSize(this IRarHeader header)
    {
        return (Int64)header.GetReflectionProperty("UncompressedSize")!;
    }

    public static String GetFileName(this IRarHeader header)
    {
        return (String)header.GetReflectionProperty("FileName")!;
    }

    public static Boolean IsDirectory(this IRarHeader header)
    {
        return (Boolean)header.GetReflectionProperty("IsDirectory")!;
    }

    public static Int32? GetVolumeNumber(this IRarHeader header)
    {
        return header.HeaderType == HeaderType.Archive
            ? (Int32?)header.GetReflectionProperty("VolumeNumber")
            : (Int16?)header.GetReflectionProperty("VolumeNumber");
    }

    public static Boolean GetIsFirstVolume(this IRarHeader header)
    {
        return (Boolean)header.GetReflectionProperty("IsFirstVolume")!;
    }

    public static Byte[]? GetR4Salt(this IRarHeader header)
    {
        return (Byte[]?)header.GetReflectionProperty("R4Salt")!;
    }

    public static Object? GetRar5CryptoInfo(this IRarHeader header)
    {
        return header.GetReflectionProperty("Rar5CryptoInfo")!;
    }

    public static Boolean GetIsEncrypted(this IRarHeader header)
    {
        return (Boolean)header.GetReflectionProperty("IsEncrypted")!;
    }

    public static Boolean GetIsSolid(this IRarHeader header)
    {
        return (Boolean)header.GetReflectionProperty("IsSolid")!;
    }

    public static AesParams? GetAesParams(this IRarHeader header, String? password)
    {
        // sanity checks
        if (header.HeaderType != HeaderType.File) return null;
        if (password == null) return null;

        // rar3 aes params
        var r4Salt = header.GetR4Salt();
        if (r4Salt != null) return GetRar3AesParams(r4Salt, password, header.GetUncompressedSize());

        // rar5 aes params
        var rar5CryptoInfo = header.GetRar5CryptoInfo();
        if (rar5CryptoInfo != null) return GetRar5AesParams(rar5CryptoInfo, password, header.GetUncompressedSize());

        // no aes params
        return null;
    }

    private static AesParams? GetRar3AesParams(Byte[] salt, String password, Int64 decodedSize)
    {
        const Int32 sizeInitV = 0x10;
        const Int32 sizeSalt30 = 0x08;
        var aesIV = new byte[sizeInitV];

        var rawLength = 2 * password.Length;
        var rawPassword = new byte[rawLength + sizeSalt30];
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        for (var i = 0; i < password.Length; i++)
        {
            rawPassword[i * 2] = passwordBytes[i];
            rawPassword[(i * 2) + 1] = 0;
        }

        for (var i = 0; i < salt.Length; i++)
        {
            rawPassword[i + rawLength] = salt[i];
        }

        var msgDigest = SHA1.Create();
        const Int32 noOfRounds = (1 << 18);
        const Int32 iblock = 3;

        byte[] digest;
        var data = new byte[(rawPassword.Length + iblock) * noOfRounds];

        //TODO slow code below, find ways to optimize
        for (var i = 0; i < noOfRounds; i++)
        {
            rawPassword.CopyTo(data, i * (rawPassword.Length + iblock));

            data[(i * (rawPassword.Length + iblock)) + rawPassword.Length + 0] = (Byte)i;
            data[(i * (rawPassword.Length + iblock)) + rawPassword.Length + 1] = (Byte)(i >> 8);
            data[(i * (rawPassword.Length + iblock)) + rawPassword.Length + 2] = (Byte)(i >> 16);

            if (i % (noOfRounds / sizeInitV) == 0)
            {
                digest = msgDigest.ComputeHash(data, 0, (i + 1) * (rawPassword.Length + iblock));
                aesIV[i / (noOfRounds / sizeInitV)] = digest[19];
            }
        }

        digest = msgDigest.ComputeHash(data);
        //slow code ends

        var aesKey = new Byte[sizeInitV];
        for (var i = 0; i < 4; i++)
        {
            for (var j = 0; j < 4; j++)
            {
                aesKey[(i * 4) + j] = (Byte)(
                    (
                        ((digest[i * 4] * 0x1000000) & 0xff000000)
                        | (UInt32)((digest[(i * 4) + 1] * 0x10000) & 0xff0000)
                        | (UInt32)((digest[(i * 4) + 2] * 0x100) & 0xff00)
                        | (UInt32)(digest[(i * 4) + 3] & 0xff)
                    ) >> (j * 8)
                );
            }
        }

        return new AesParams()
        {
            Iv = aesIV,
            Key = aesKey,
            DecodedSize = decodedSize,
        };
    }

    private static AesParams? GetRar5AesParams(Object rar5CryptoInfo, String password, Int64 decodedSize)
    {
        const Int32 derivedKeyLength = 0x10;
        const Int32 sizePswCheck = 0x08;
        const Int32 sha256DigestSize = 32;
        var lg2Count = (Int32)rar5CryptoInfo.GetReflectionField("LG2Count")!;
        var salt = (Byte[])rar5CryptoInfo.GetReflectionField("Salt")!;
        var usePswCheck = (Boolean)rar5CryptoInfo.GetReflectionField("UsePswCheck")!;
        var pswCheck = (Byte[])rar5CryptoInfo.GetReflectionField("PswCheck")!;
        var initIv = (Byte[])rar5CryptoInfo.GetReflectionField("InitV")!;

        var iterations = (1 << lg2Count);

        var salt_rar5 = salt.Concat(new Byte[] { 0, 0, 0, 1 });
        var derivedKey = GenerateRarPbkdf2Key(
            password,
            salt_rar5.ToArray(),
            iterations,
            derivedKeyLength
        );

        var derivedPswCheck = new Byte[sizePswCheck];
        for (var i = 0; i < sha256DigestSize; i++)
        {
            derivedPswCheck[i % sizePswCheck] ^= derivedKey[2][i];
        }

        if (usePswCheck && !pswCheck.SequenceEqual(derivedPswCheck))
        {
            throw new CryptographicException("The password did not match.");
        }

        return new AesParams()
        {
            Iv = initIv,
            Key = derivedKey[0],
            DecodedSize = decodedSize,
        };
    }


    private static List<Byte[]> GenerateRarPbkdf2Key(
        String password,
        Byte[] salt,
        Int32 iterations,
        Int32 keyLength
    )
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(password));
        var block = hmac.ComputeHash(salt);
        var finalHash = (Byte[])block.Clone();

        var loop = new Int32[] { iterations, 17, 17 };
        var res = new List<Byte[]> { };

        for (var x = 0; x < 3; x++)
        {
            for (var i = 1; i < loop[x]; i++)
            {
                block = hmac.ComputeHash(block);
                for (var j = 0; j < finalHash.Length; j++)
                {
                    finalHash[j] ^= block[j];
                }
            }

            res.Add((Byte[])finalHash.Clone());
        }

        return res;
    }
}
