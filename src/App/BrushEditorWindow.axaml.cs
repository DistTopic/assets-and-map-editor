using Avalonia.Controls;
using POriginsItemEditor.App.ViewModels;

namespace POriginsItemEditor.App;

public partial class BrushEditorWindow : Window
{
    public BrushEditorWindow()
    {
        InitializeComponent();
    }

    public BrushEditorWindow(BrushEditorViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }
}
