namespace cambiadns;

using System;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Security.Principal;
using System.Threading.Tasks;

class Program
{
    static async Task Main(string[] args)
    {
        // Verifica e rilancio come amministratore se necessario
        if (!IsAdministrator())
        {
            Console.WriteLine("Riavvio con privilegi di amministratore...");
            var processInfo = new ProcessStartInfo
            {
                FileName = Environment.ProcessPath,
                UseShellExecute = true,
                Verb = "runas" // Richiede elevazione dei privilegi
            };

            try
            {
                Process.Start(processInfo);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Errore: impossibile ottenere i privilegi di amministratore. " +
                                  $"Dettagli: {ex.Message}");
            }

            return; // Esce dal processo originale
        }

        Console.WriteLine("Privilegi di amministratore confermati.\n");

        // Step 1: Mostra gli adattatori di rete connessi a Internet
        Console.WriteLine("Elenco delle schede di rete connesse a Internet:\n");

        var adapters = NetworkInterface.GetAllNetworkInterfaces()
            .Where(ni =>
                ni.OperationalStatus == OperationalStatus.Up && // Stato attivo
                (ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet ||
                 ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) && // Ethernet o Wi-Fi
                ni.GetIPProperties().GatewayAddresses.Any(g => g.Address.ToString() != "0.0.0.0")) // Gateway configurato
            .OrderBy(ni => ni.Name)
            .ToList();

        if (!adapters.Any())
        {
            Console.WriteLine("Nessuna scheda di rete connessa a Internet trovata.");
            return;
        }

        for (int i = 0; i < adapters.Count; i++)
        {
            var adapter = adapters[i];
            Console.WriteLine($"{i + 1}. Nome: {adapter.Name}");
            Console.WriteLine($"   Descrizione: {adapter.Description}");
            Console.WriteLine($"   Tipo: {adapter.NetworkInterfaceType}");
            Console.WriteLine($"   Stato: {adapter.OperationalStatus}\n");
        }

        Console.WriteLine("Seleziona il numero dell'adattatore per configurare i DNS:");
        if (!int.TryParse(Console.ReadLine(), out int selectedIndex) ||
            selectedIndex < 1 || selectedIndex > adapters.Count)
        {
            Console.WriteLine("Selezione non valida.");
            return;
        }

        // Ottieni il nome della scheda selezionata
        string selectedAdapterName = adapters[selectedIndex - 1].Name;
        Console.WriteLine($"\nHai selezionato: {selectedAdapterName}");

        // Step 2: Chiedi se impostare DNS manuali o ripristinare l'automatico
        Console.WriteLine("\nVuoi impostare un nuovo DNS o ripristinare la configurazione automatica?");
        Console.WriteLine("   1. Imposta un nuovo DNS");
        Console.WriteLine("   2. Ripristina DNS automatico");
        string choice = Console.ReadLine();

        if (choice == "2")
        {
            try
            {
                await SetAutomaticDnsAsync(selectedAdapterName);
                Console.WriteLine("DNS ripristinato alla configurazione automatica con successo.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Errore durante il ripristino del DNS automatico: {ex.Message}");
            }
            return;
        }

        if (choice != "1")
        {
            Console.WriteLine("Scelta non valida. Operazione annullata.");
            return;
        }

        // Step 3: Chiedi i DNS all'utente
        Console.WriteLine("\nInserisci il DNS primario (premi Invio per usare il valore predefinito: 1.1.1.1):");
        string primaryDns = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(primaryDns))
        {
            primaryDns = "1.1.1.1";
        }

        Console.WriteLine("Inserisci il DNS secondario (premi Invio per usare il valore predefinito: 8.8.8.8):");
        string secondaryDns = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(secondaryDns))
        {
            secondaryDns = "8.8.8.8";
        }

        Console.WriteLine($"\nImpostazione DNS su {selectedAdapterName} con:");
        Console.WriteLine($"   DNS Primario: {primaryDns}");
        Console.WriteLine($"   DNS Secondario: {secondaryDns}");
        try
        {
            await SetDnsAsync(selectedAdapterName, primaryDns, secondaryDns);
            Console.WriteLine("DNS configurati con successo.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Errore durante la configurazione dei DNS: {ex.Message}");
        }
    }

    static bool IsAdministrator()
    {
        var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    static async Task SetDnsAsync(string adapterName, string primaryDns, string? secondaryDns)
    {
        // Comando per configurare il DNS primario
        string netshCommandPrimary = $"interface ip set dns name=\"{adapterName}\" source=static addr={primaryDns}";
        await RunCommandAsync("netsh", netshCommandPrimary);

        // Configura il DNS secondario solo se fornito
        if (!string.IsNullOrWhiteSpace(secondaryDns))
        {
            string netshCommandSecondary = $"interface ip add dns name=\"{adapterName}\" addr={secondaryDns} index=2";
            await RunCommandAsync("netsh", netshCommandSecondary);
        }
    }

    static async Task SetAutomaticDnsAsync(string adapterName)
    {
        // Comando per ripristinare il DNS automatico
        string netshCommand = $"interface ip set dns name=\"{adapterName}\" source=dhcp";
        await RunCommandAsync("netsh", netshCommand);
    }

    static async Task RunCommandAsync(string fileName, string arguments)
    {
        var processStartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(processStartInfo);
        if (process == null)
        {
            throw new InvalidOperationException("Impossibile avviare il processo.");
        }

        string output = await process.StandardOutput.ReadToEndAsync();
        string error = await process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Errore durante l'esecuzione del comando: {error}");
        }

        Console.WriteLine(output);
    }
}
