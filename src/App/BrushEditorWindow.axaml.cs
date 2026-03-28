using Avalonia.Controls;
using AssetsAndMapEditor.App.ViewModels;

namespace AssetsAndMapEditor.App;

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
