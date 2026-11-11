// Stand-in for PsExec.exe used by the LiteDeploy soak test
// (tools/soak/Measure-LiteDeploy.ps1). It never touches the network or the
// remote computer: it prints the kind of output LiteDeploy parses (process
// started / exited, host not reachable) and some command output, then exits.
//
// Compiled on the fly by the soak script with the .NET Framework csc.exe.
using System;
using System.Threading;

static class PsExecStub
{
    static int Main(string[] args)
    {
        string host = "unknown";
        bool detached = false;

        foreach (string a in args)
        {
            if (a.StartsWith(@"\\") && host == "unknown")
                host = a.Substring(2);
            else if (a == "-d")
                detached = true;
        }

        Console.Error.WriteLine();
        Console.Error.WriteLine("PsExec stand-in for the LiteDeploy soak test");
        Console.Error.WriteLine();

        // Some time "on the network", like a real remote start
        Thread.Sleep(150);

        if (host.StartsWith("SOAK-ERR", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("Couldn't access " + host + ":");
            Console.Error.WriteLine("The network path was not found.");
            return 53;
        }

        // Command output (kept by LiteDeploy for the process report)
        for (int i = 1; i <= 40; i++)
            Console.Out.WriteLine("soak output line " + i + " from " + host);

        if (detached)
            Console.Error.WriteLine("cmd started on " + host + " with process ID " + (1000 + host.Length) + ".");
        else
            Console.Error.WriteLine("cmd exited on " + host + " with error code 0.");

        return 0;
    }
}
