using Salon.Infrastructure;

namespace Salon.Api;

public static class ProvisionCommand
{
    public static async Task<int> Run(IServiceProvider services, string[] args)
    {
        if (args.Length is < 3 or > 4 || (args.Length == 4 && !Guid.TryParse(args[3], out _)))
        {
            Console.Error.WriteLine("Usage: provision <login> <OwnerAdmin|Manager|Employee> [salon-guid]");
            return 1;
        }
        var password = Environment.GetEnvironmentVariable("SALON_BOOTSTRAP_PASSWORD");
        if (password is null && !Console.IsInputRedirected)
        {
            Console.Write("Initial password (hidden): ");
            var buffer = new System.Text.StringBuilder();
            while (true)
            {
                var key = Console.ReadKey(intercept: true);
                if (key.Key == ConsoleKey.Enter) break;
                if (key.Key == ConsoleKey.Backspace) { if (buffer.Length > 0) buffer.Length--; }
                else if (!char.IsControl(key.KeyChar)) buffer.Append(key.KeyChar);
            }
            Console.WriteLine();
            password = buffer.ToString();
        }
        if (string.IsNullOrEmpty(password))
        {
            Console.Error.WriteLine("Supply SALON_BOOTSTRAP_PASSWORD or use the interactive hidden prompt.");
            return 1;
        }
        using var scope = services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<AccountProvisioner>()
            .Provision(args[1], args[2], args.Length == 4 ? Guid.Parse(args[3]) : null, password);
        if (result.Succeeded) { Console.WriteLine("Account created."); return 0; }
        foreach (var error in result.Errors) Console.Error.WriteLine(error.Description);
        return 1;
    }
}
