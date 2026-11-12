using Avalonia;
using Avalonia.Controls;

namespace CncCad.App.Views;

public partial class TaskPanelHost:UserControl
{
    public static readonly StyledProperty<object?> HeaderContentProperty=AvaloniaProperty.Register<TaskPanelHost,object?>(nameof(HeaderContent));
    public static readonly StyledProperty<object?> BodyProperty=AvaloniaProperty.Register<TaskPanelHost,object?>(nameof(Body));
    public static readonly StyledProperty<object?> FooterProperty=AvaloniaProperty.Register<TaskPanelHost,object?>(nameof(Footer));
    public object? HeaderContent{get=>GetValue(HeaderContentProperty);set=>SetValue(HeaderContentProperty,value);}
    public object? Body{get=>GetValue(BodyProperty);set=>SetValue(BodyProperty,value);}
    public object? Footer{get=>GetValue(FooterProperty);set=>SetValue(FooterProperty,value);}
    public TaskPanelHost()=>InitializeComponent();
}
