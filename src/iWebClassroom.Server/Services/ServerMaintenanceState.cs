namespace IWebClassroom.Server.Services;

public sealed class ServerMaintenanceState
{
    private int _updateDrain;

    public bool IsUpdateDrain => Volatile.Read(ref _updateDrain) == 1;

    public void BeginUpdateDrain() => Interlocked.Exchange(ref _updateDrain, 1);

    public void CancelUpdateDrain() => Interlocked.Exchange(ref _updateDrain, 0);
}
