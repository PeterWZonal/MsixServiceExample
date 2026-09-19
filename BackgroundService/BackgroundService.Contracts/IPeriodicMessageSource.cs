using System;

namespace BackgroundService.Contracts
{
    /// <summary>
    /// Contract implemented by plugins (e.g. shipped in a modification package) that the service
    /// discovers at startup and polls for a message every <see cref="Interval"/>.
    /// </summary>
    public interface IPeriodicMessageSource
    {
        string Name { get; }

        TimeSpan Interval { get; }

        string GetMessage();
    }
}
