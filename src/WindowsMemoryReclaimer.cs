using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Threading;

/// <summary>Trim only this launcher's resident pages at quiet lifecycle boundaries.</summary>
internal sealed partial class WindowsMemoryReclaimer : IDisposable
{
    private readonly DispatcherTimer delay = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly Func<bool> canReclaim;
    private bool disposed;
    internal int Attempts { get; private set; }
    internal bool LastSucceeded { get; private set; }
    internal int LastError { get; private set; }
    internal long WorkingSetBefore { get; private set; }
    internal long WorkingSetAfter { get; private set; }

    internal WindowsMemoryReclaimer(Func<bool> canReclaim)
    {
        this.canReclaim = canReclaim;
        delay.Tick += Reclaim;
    }

    internal void Request()
    {
        if (disposed || !OperatingSystem.IsWindows()) return;
        delay.Stop();
        delay.Start();
    }

    private void Reclaim(object? sender, EventArgs args)
    {
        delay.Stop();
        if (disposed || !OperatingSystem.IsWindows() || !canReclaim()) return;
        Attempts++;
        // The -1 pseudo handle names only the current process and is not owned.
        // This evicts resident pages; it does not free allocations/private commit.
        // Never run on a periodic timer or trim the game or other processes.
        using var process = Process.GetCurrentProcess();
        WorkingSetBefore = process.WorkingSet64;
        LastSucceeded = EmptyWorkingSet(new nint(-1)) != 0;
        LastError = LastSucceeded ? 0 : Marshal.GetLastPInvokeError();
        process.Refresh();
        WorkingSetAfter = process.WorkingSet64;
        if (!LastSucceeded)
            Console.Error.WriteLine($"Windows working-set trim unavailable: {LastError}");
    }

    public void Dispose()
    {
        disposed = true;
        delay.Stop();
        delay.Tick -= Reclaim;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "K32EmptyWorkingSet", SetLastError = true)]
    private static partial int EmptyWorkingSet(nint process);
}
