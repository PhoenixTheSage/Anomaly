using System.Text;
using ClientPlugin.Settings.Tools;
using Sandbox;
using Sandbox.Graphics.GUI;
using VRage.Utils;
using VRageMath;

namespace ClientPlugin.Settings;

internal class StatusScreen : MyGuiScreenBase
{
    const float DefaultTextScale = 0.7f;
    const float DebugTextScale = 0.58f;
    const string TextFont = "Blue";

    readonly string bodyText;
    readonly string caption;
    readonly string friendlyName;
    readonly bool debugLayout;

    public StatusScreen(string text)
        : this("Anomaly Status", "AnomalyStatus", text)
    {
    }

    public StatusScreen(string title, string name, string text)
        : base(
            new Vector2(0.5f, 0.5f),
            MyGuiConstants.SCREEN_BACKGROUND_COLOR,
            GetScreenSize(name),
            false,
            null,
            MySandboxGame.Config.UIBkOpacity,
            MySandboxGame.Config.UIOpacity)
    {
        caption = string.IsNullOrWhiteSpace(title) ? "Anomaly Status" : title;
        friendlyName = string.IsNullOrWhiteSpace(name) ? "AnomalyStatus" : name;
        debugLayout = string.Equals(friendlyName, "AnomalyDebugStatus", System.StringComparison.Ordinal);
        bodyText = text ?? "";
        EnabledBackgroundFade = true;
        m_closeOnEsc = true;
        m_drawEvenWithoutFocus = true;
        CanHideOthers = true;
        CanBeHidden = true;
        CloseButtonEnabled = true;
    }

    public override string GetFriendlyName() => friendlyName;

    public override void LoadContent()
    {
        base.LoadContent();
        RecreateControls(true);
    }

    public override void RecreateControls(bool constructor)
    {
        base.RecreateControls(constructor);
        AddCaption(caption);

        var screenSize = Size ?? GetScreenSize(friendlyName);
        var textScale = debugLayout ? DebugTextScale : DefaultTextScale;
        var topInset = debugLayout ? 0.075f : 0.09f;
        var bottomInset = debugLayout ? 0.075f : 0.09f;
        var textSize = new Vector2(screenSize.X - 0.06f, screenSize.Y - topInset - bottomInset);
        var wrapWidth = textSize.X - 0.04f;
        string wrapped;
        try
        {
            wrapped = DescriptionToolTip.WrapText(bodyText, wrapWidth, textScale, TextFont);
        }
        catch
        {
            wrapped = bodyText;
        }

        var body = new MyGuiControlMultilineText(
            position: new Vector2(0f, -screenSize.Y * 0.5f + topInset),
            size: textSize,
            backgroundColor: null,
            font: TextFont,
            textScale: textScale,
            textAlign: MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP,
            contents: new StringBuilder(wrapped),
            drawScrollbarV: true,
            drawScrollbarH: false,
            textBoxAlign: MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP)
        {
            OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_TOP,
        };
        Controls.Add(body);

        var ok = new MyGuiControlButton(
            text: new StringBuilder("OK"),
            onButtonClick: _ => CloseScreen())
        {
            OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_BOTTOM,
            Position = new Vector2(0f, screenSize.Y * 0.5f - 0.03f),
        };
        Controls.Add(ok);
    }

    static Vector2 GetScreenSize(string name) =>
        string.Equals(name, "AnomalyDebugStatus", System.StringComparison.Ordinal)
            ? new Vector2(0.82f, 0.84f)
            : new Vector2(0.72f, 0.78f);
}
