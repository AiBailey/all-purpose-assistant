using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AllPurposeAssistant.Views;

public partial class ScreenshotOverlayWindow
{
    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        if (IsVisible && _state == OverlayState.Selecting)
            Close();
    }

    private bool TryHandleInlineKey(KeyEventArgs e)
    {
        if (_state != OverlayState.Editing)
            return false;

        if (e.Key == Key.Escape)
        {
            if (Keyboard.FocusedElement is TextBox)
            {
                AnnotateCanvas.Focus();
                e.Handled = true;
                return true;
            }

            FinishInlineSession();
            e.Handled = true;
            return true;
        }

        HandleEditorKeyDown(e);
        return e.Handled;
    }
}
