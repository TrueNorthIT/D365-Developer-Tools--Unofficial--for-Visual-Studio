using System;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Shared.Mvvm;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.PluginDebugging
{
    /// <summary>Backs PluginDebuggingControl's non-modal session status strip — set for the lifetime of one "Debug This" run.</summary>
    internal sealed class DebugSessionInfo : ObservableObject
    {
        public string Label { get; set; }
        public DateTime StartedAtUtc { get; set; }

        private string _statusText;
        public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }
    }
}
