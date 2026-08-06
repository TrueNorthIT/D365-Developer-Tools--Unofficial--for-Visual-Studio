using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.Shared
{
    /// <summary>
    /// Wraps IVsInfoBar for non-blocking success/status notices attached to a tool window frame —
    /// the closest non-modal-toast parity to vscode.window.showInformationMessage.
    /// </summary>
    internal sealed class D365InfoBar : IVsInfoBarUIEvents
    {
        private readonly IVsInfoBarUIElement _uiElement;
        private uint _cookie;

        private D365InfoBar(IVsInfoBarUIElement uiElement)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _uiElement = uiElement;
            _uiElement.Advise(this, out _cookie);
        }

        public static void Show(IVsWindowFrame frame, string message)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (frame.GetProperty((int)__VSFPROPID7.VSFPROPID_InfoBarHost, out var hostObj) != Microsoft.VisualStudio.VSConstants.S_OK
                || !(hostObj is IVsInfoBarHost host))
            {
                return;
            }

            if (!(ServiceProvider.GlobalProvider.GetService(typeof(SVsInfoBarUIFactory)) is IVsInfoBarUIFactory factory))
            {
                return;
            }

            var model = new InfoBarModel(message, isCloseButtonVisible: true);
            var uiElement = factory.CreateInfoBar(model);
            var infoBar = new D365InfoBar(uiElement);
            host.AddInfoBar(uiElement);
        }

        void IVsInfoBarUIEvents.OnClosed(IVsInfoBarUIElement infoBarUIElement)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            infoBarUIElement.Unadvise(_cookie);
        }

        void IVsInfoBarUIEvents.OnActionItemClicked(IVsInfoBarUIElement infoBarUIElement, IVsInfoBarActionItem actionItem) { }
    }
}
