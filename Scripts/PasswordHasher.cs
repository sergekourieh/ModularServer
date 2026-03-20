using System.Security.Cryptography;

public class PasswordHasher {
    private const int SaltSize = 16; // 128-bit salt
    private const int KeySize = 32;  // 256-bit hash
    private const int Iterations = 100000; // slow enough to be secure

    // Hash password
    public static string HashPassword(string password) {
        using (var rng = RandomNumberGenerator.Create()) {
            var salt = new byte[SaltSize];
            rng.GetBytes(salt);

            using (var pbkdf2 = new Rfc2898DeriveBytes(password, salt, Iterations, HashAlgorithmName.SHA256)) {
                var hash = pbkdf2.GetBytes(KeySize);
                var result = new byte[SaltSize + KeySize];
                Array.Copy(salt, 0, result, 0, SaltSize);
                Array.Copy(hash, 0, result, SaltSize, KeySize);
                return Convert.ToBase64String(result);
            }
        }
    }

    // Verify password
    public static bool VerifyPassword(string password, string storedHash) {
        var bytes = Convert.FromBase64String(storedHash);
        var salt = new byte[SaltSize];
        Array.Copy(bytes, 0, salt, 0, SaltSize);

        using (var pbkdf2 = new Rfc2898DeriveBytes(password, salt, Iterations, HashAlgorithmName.SHA256)) {
            var hash = pbkdf2.GetBytes(KeySize);
            for (int i = 0; i < KeySize; i++) {
                if (bytes[i + SaltSize] != hash[i])
                    return false;
            }
        }
        return true;
    }
}

// https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.rfc2898derivebytes?view=net-9.0