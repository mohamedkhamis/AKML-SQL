namespace AkmlSql.Site.Admin;

/// <summary>
/// <c>AkmlSql.Site --hash-password</c>: prompts for a password and prints the value to set as the
/// server's <c>Admin__PasswordHash</c> environment variable.
/// <para>
/// This lives in the site executable on purpose. The hash format and work factor are owned by
/// <see cref="AdminAuth"/>, so generating with a separate script could drift from what verifies it
/// (OPS-001: an admin portal that cannot be signed into is exactly the failure this prevents).
/// </para>
/// </summary>
public static class AdminPasswordTool
{
    /// <summary>Minimum length accepted — one shared credential guards the whole portal.</summary>
    public const int MinimumPasswordLength = 12;

    /// <summary>Runs the prompt. Returns 0 on success, 1 when the input was rejected.</summary>
    public static int Run()
    {
        Console.WriteLine("AKML SQL — admin password hash generator");
        Console.WriteLine();

        var password = ReadSecret("Password: ");
        if (string.IsNullOrEmpty(password))
        {
            Console.Error.WriteLine("No password entered — nothing generated.");
            return 1;
        }

        if (password.Length < MinimumPasswordLength)
        {
            Console.Error.WriteLine($"Password must be at least {MinimumPasswordLength} characters.");
            return 1;
        }

        if (!string.Equals(password, ReadSecret("Confirm:  "), StringComparison.Ordinal))
        {
            Console.Error.WriteLine("Passwords did not match — nothing generated.");
            return 1;
        }

        var hash = AdminAuth.HashPassword(password);

        Console.WriteLine();
        Console.WriteLine("Set this on the server (app pool environment variable, NOT a file under");
        Console.WriteLine("the deploy path — the deploy mirror would erase it):");
        Console.WriteLine();
        Console.WriteLine($"  Admin__PasswordHash={hash}");
        Console.WriteLine();
        return 0;
    }

    /// <summary>
    /// Reads a line without echoing it. Falls back to a plain read when the console is redirected
    /// (<see cref="Console.ReadKey()"/> throws without a real input handle).
    /// </summary>
    private static string ReadSecret(string prompt)
    {
        Console.Write(prompt);

        if (Console.IsInputRedirected)
        {
            return Console.ReadLine() ?? "";
        }

        var builder = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return builder.ToString();
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (builder.Length > 0)
                {
                    builder.Length--;
                }

                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                builder.Append(key.KeyChar);
            }
        }
    }
}
