using System.Security.Cryptography;

namespace Shortly.Domain;

public static class ShortCodeGenerator
{
    private const string Alphabet = "23456789abcdefghijkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ";

    public static string Next(int length = 7)
    {
        if (length is < 4 or > 32)
            throw new ArgumentOutOfRangeException(nameof(length));

        var buffer = new char[length];
        Span<byte> bytes = stackalloc byte[length];
        RandomNumberGenerator.Fill(bytes);

        for (var i = 0; i < length; i++)
            buffer[i] = Alphabet[bytes[i] % Alphabet.Length];

        return new string(buffer);
    }
}