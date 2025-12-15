using System;
using Avalonia.Controls;
using Avalonia.Controls.Models.TreeDataGrid;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using PlantCad.Gui.Services;
using PlantCad.Gui.ViewModels.Tools;

namespace PlantCad.Gui.Views.Tools;

public partial class LayersToolView : UserControl
{
    private ScrollBar? _verticalScrollBar;
    private bool _userControllingScroll;

    public LayersToolView()
    {
        InitializeComponent();
        // Ensure the Layers tool is bound to the LayersToolViewModel
        if (
            DataContext is not LayersToolViewModel
            && ServiceRegistry.LayersTool is LayersToolViewModel layersVm
        )
        {
            DataContext = layersVm;
        }
        if (DataContext is LayersToolViewModel vm)
        {
            // Behavior attached in XAML now handles BringIntoViewRequested and scrolling
        }

        // Wire up auto-scroll-to-end behavior on the TreeDataGrid
        this.AttachedToVisualTree += (_, __) => AttachAutoScroll();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void AttachAutoScroll()
    {
        var tree = this.FindControl<TreeDataGrid>("Tree");
        if (tree == null)
            return;

        tree.TemplateApplied += (_, args) =>
        {
            try
            {
                _verticalScrollBar = args.NameScope.Find<ScrollBar>("PART_VerticalScrollbar");
                if (_verticalScrollBar != null)
                {
                    _verticalScrollBar.PointerEntered += (_, __) => _userControllingScroll = true;
                    _verticalScrollBar.PointerExited += (_, __) => _userControllingScroll = false;
                    tree.LayoutUpdated += (_, __) =>
                    {
                        if (_verticalScrollBar == null)
                            return;
                        if (_userControllingScroll)
                            return;
                        // If not at the end, keep auto-scrolling to the end when content grows
                        if (_verticalScrollBar.Maximum - _verticalScrollBar.Value > 2)
                        {
                            _verticalScrollBar.Value = _verticalScrollBar.Maximum;
                        }
                    };
                }
                else
                {
                    ServiceRegistry.LogsTool?.Append(
                        $"[{DateTime.Now:HH:mm:ss}] WARN LayersToolView: vertical scrollbar not found for auto-scroll."
                    );
                }
            }
            catch (Exception ex)
            {
                ServiceRegistry.LogsTool?.Append(
                    $"[{DateTime.Now:HH:mm:ss}] WARN LayersToolView auto-scroll: {ex.Message}"
                );
            }
        };
    }
}
