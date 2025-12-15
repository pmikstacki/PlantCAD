using Avalonia;
using Avalonia.Input;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using IconPacks.Avalonia.Material;

namespace PlantCad.Gui.Controls.Modes
{
    public sealed class ModuleEditMode : IViewportMode
    {
        private readonly Services.Modules.ModuleShapeEditor _editor;
        private readonly System.Action<System.Collections.Generic.IReadOnlyList<Point>>? _requestFinish;
        private readonly System.Action? _requestCancel;
        private readonly string? _moduleName;
        private Controls.CadViewportControl? _viewport;
        private StackPanel? _rootHud;
        private StackPanel? _vertexPanel;
        private Popup? _vertexPopup;
        private System.Func<System.Collections.Generic.IEnumerable<(Point pos, string text)>>? _baseLabelsProvider;
        private Button? _splitBtnPanel;
        private Button? _deleteBtnPanel;
        private Button? _splitBtnPopup;
        private Button? _deleteBtnPopup;
        private System.Func<System.Collections.Generic.IEnumerable<(Point pos, string text, string id)>>? _baseCardsProvider;

        public ModuleEditMode(
            Services.Modules.ModuleShapeEditor editor,
            System.Action<System.Collections.Generic.IReadOnlyList<Point>>? requestFinish = null,
            System.Action? requestCancel = null,
            string? moduleName = null
        )
        {
            _editor = editor ?? throw new System.ArgumentNullException(nameof(editor));
            _requestFinish = requestFinish;
            _requestCancel = requestCancel;
            _moduleName = moduleName;
        }

        public void OnEnter(Controls.CadViewportControl viewport)
        {
            _viewport = viewport;
            _editor.WireOverlay();
            // Build HUD: title + snap + finish/cancel and vertex actions
            _rootHud = new StackPanel { Orientation = Orientation.Vertical, Spacing = 6, Margin = new Thickness(8) };
            var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var snap = new CheckBox { Content = "Snap", IsChecked = _editor.SnapToGrid };
            snap.IsCheckedChanged += (_, __) =>
            {
                _editor.SnapToGrid = snap.IsChecked == true;
                _viewport?.RequestInvalidate();
            };
            var show = new CheckBox { Content = "Show modules", IsChecked = Controls.Rendering.ModulesOverlayRenderer.Visible };
            show.IsCheckedChanged += (_, __) =>
            {
                Controls.Rendering.ModulesOverlayRenderer.Visible = show.IsChecked == true;
                _viewport?.RequestInvalidate();
            };
            var finishBtn = new Button { Content = "Finish" };
            finishBtn.Classes.Add("viewport-action");
            finishBtn.Classes.Add("primary");
            finishBtn.Click += (_, __) =>
            {
                if (_editor.TryFinish(out var pts))
                {
                    _requestFinish?.Invoke(pts);
                }
            };
            var cancelBtn = new Button { Content = "Cancel" };
            cancelBtn.Classes.Add("viewport-action");
            cancelBtn.Classes.Add("danger");
            cancelBtn.Click += (_, __) =>
            {
                _editor.Cancel();
                _requestCancel?.Invoke();
            };
            bar.Children.Add(snap);
            bar.Children.Add(finishBtn);
            bar.Children.Add(cancelBtn);
            bar.Children.Add(show);

            _vertexPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            _deleteBtnPanel = new Button { Content = "Delete point" };
            _deleteBtnPanel.Classes.Add("viewport-action");
            _deleteBtnPanel.Classes.Add("info");
            _deleteBtnPanel.Click += (_, __) =>
            {
                if (_editor.DeleteSelectedPoint())
                {
                    _vertexPanel!.IsVisible = _editor.SelectedHandleIndex >= 0;
                    UpdateContextUi();
                }
            };
            _splitBtnPanel = new Button { Content = "Split edge" };
            _splitBtnPanel.Classes.Add("viewport-action");
            _splitBtnPanel.Classes.Add("violet");
            _splitBtnPanel.Click += (_, __) =>
            {
                if (_editor.InsertMidpointOnHoveredEdge())
                {
                    _vertexPanel!.IsVisible = _editor.SelectedHandleIndex >= 0;
                    UpdateContextUi();
                }
            };
            _vertexPanel.Children.Add(_deleteBtnPanel);
            _vertexPanel.Children.Add(_splitBtnPanel);
            _vertexPanel.IsVisible = _editor.SelectedHandleIndex >= 0;

            // Use standardized viewport panels for consistent look and icons
            var mainPanel = new PlantCad.Gui.Controls.Viewport.ViewportPanel
            {
                Header = "Module Mode",
                Icon = new PackIconMaterial { Kind = PackIconMaterialKind.ShapePolygonPlus, Width = 18, Height = 18, Foreground = Brushes.White },
                Content = bar
            };
            var vertexPanelHost = new PlantCad.Gui.Controls.Viewport.ViewportPanel
            {
                Header = "Vertex",
                Icon = new PackIconMaterial { Kind = PackIconMaterialKind.VectorPoint, Width = 18, Height = 18, Foreground = Brushes.White },
                Content = _vertexPanel
            };

            _rootHud.Children.Add(mainPanel);
            _rootHud.Children.Add(vertexPanelHost);
            viewport.HudContent = _rootHud;

            _vertexPopup = new Popup
            {
                PlacementTarget = viewport,
                IsOpen = true,
                Child = BuildVertexPopupContent(),
            };
            UpdateVertexPopupPosition();
            UpdateContextUi();
            _baseLabelsProvider = Controls.Rendering.ModulesOverlayRenderer.LabelsProvider;
            Controls.Rendering.ModulesOverlayRenderer.LabelsProvider = () =>
            {
                var list = new System.Collections.Generic.List<(Point pos, string text)>();
                var baseProv = _baseLabelsProvider;
                if (baseProv != null)
                {
                    foreach (var l in baseProv()) list.Add(l);
                }
                var curr = _editor.Current;
                if (_editor.IsActive && curr != null && curr.Count >= 2)
                {
                    double cx = 0, cy = 0;
                    for (int i = 0; i < curr.Count; i++)
                    {
                        cx += curr[i].X;
                        cy += curr[i].Y;
                    }
                    cx /= curr.Count;
                    cy /= curr.Count;
                    list.Add((new Point(cx, cy), _moduleName ?? "Module"));
                }
                return list;
            };

            _baseCardsProvider = Controls.Rendering.ModulesOverlayRenderer.CardsProvider;
            Controls.Rendering.ModulesOverlayRenderer.CardsProvider = () =>
            {
                var list = new System.Collections.Generic.List<(Point pos, string text, string id)>();
                var baseProv = _baseCardsProvider;
                if (baseProv != null)
                {
                    foreach (var c in baseProv()) list.Add(c);
                }
                var curr = _editor.Current;
                if (_editor.IsActive && curr != null && curr.Count >= 2)
                {
                    double cx = 0, cy = 0;
                    for (int i = 0; i < curr.Count; i++)
                    {
                        cx += curr[i].X;
                        cy += curr[i].Y;
                    }
                    cx /= curr.Count;
                    cy /= curr.Count;
                    // No id for in-progress polygon; click on wrench will be ignored
                    list.Add((new Point(cx, cy), _moduleName ?? "Module", string.Empty));
                }
                return list;
            };
        }

        public void OnExit(Controls.CadViewportControl viewport)
        {
            _viewport = null;
            // Clear per-edit overlay providers
            _editor.UnwireOverlay();
            viewport.HudContent = null;
            _rootHud = null;
            _vertexPanel = null;
            if (_vertexPopup != null)
            {
                _vertexPopup.IsOpen = false;
                _vertexPopup.Child = null;
                _vertexPopup = null;
            }
            Controls.Rendering.ModulesOverlayRenderer.LabelsProvider = _baseLabelsProvider;
            _baseLabelsProvider = null;
            Controls.Rendering.ModulesOverlayRenderer.CardsProvider = _baseCardsProvider;
            _baseCardsProvider = null;
        }

        public bool OnPointerPressed(Point screen)
        {
            _editor.OnPressed(screen);
            if (_vertexPanel != null)
            {
                _vertexPanel.IsVisible = _editor.SelectedHandleIndex >= 0;
            }
            UpdateVertexPopupPosition();
            UpdateContextUi();
            return true; // consume to disable default panning/select
        }

        public bool OnPointerMoved(Point screen)
        {
            _editor.OnMoved(screen);
            UpdateVertexPopupPosition();
            UpdateContextUi();
            return true; // consume to disable default hover logic when editing
        }

        public bool OnPointerReleased(Point screen)
        {
            _editor.OnReleased(screen);
            if (_vertexPanel != null)
            {
                _vertexPanel.IsVisible = _editor.SelectedHandleIndex >= 0;
            }
            UpdateVertexPopupPosition();
            UpdateContextUi();
            return true; // consume
        }

        public bool OnPointerPressedEx(Point screen, KeyModifiers mods)
        {
            _editor.OnPressedWithModifiers(screen, mods);
            return true; // consume
        }

        public bool OnKeyDown(Key key, KeyModifiers mods)
        {
            _editor.OnKeyDown(key, mods);
            return true; // consume to disable keyboard panning during edit
        }

        public bool OnWheel(PointerWheelEventArgs e)
        {
            return true; // consume wheel to disable zoom/pan while editing
        }

        private Control BuildVertexPopupContent()
        {
            var panel = new StackPanel { Orientation = Orientation.Vertical, Spacing = 4, Margin = new Thickness(6) };
            var title = new TextBlock { Text = "Vertex", FontWeight = Avalonia.Media.FontWeight.Bold, Foreground = Brushes.White };
            _deleteBtnPopup = new Button { Content = "Delete point", Foreground = Brushes.White };
            _deleteBtnPopup.Classes.Add("viewport-action");
            _deleteBtnPopup.Classes.Add("info");
            _deleteBtnPopup.Background = new SolidColorBrush(Color.FromRgb(52, 152, 219));
            _deleteBtnPopup.Click += (_, __) =>
            {
                if (_editor.DeleteSelectedPoint())
                {
                    _vertexPanel!.IsVisible = _editor.SelectedHandleIndex >= 0;
                    UpdateVertexPopupPosition();
                    UpdateContextUi();
                }
            };
            _splitBtnPopup = new Button { Content = "Split edge", Foreground = Brushes.White };
            _splitBtnPopup.Classes.Add("viewport-action");
            _splitBtnPopup.Classes.Add("violet");
            _splitBtnPopup.Background = new SolidColorBrush(Color.FromRgb(155, 89, 182));
            _splitBtnPopup.Click += (_, __) =>
            {
                if (_editor.InsertMidpointOnHoveredEdge())
                {
                    _vertexPanel!.IsVisible = _editor.SelectedHandleIndex >= 0;
                    UpdateVertexPopupPosition();
                    UpdateContextUi();
                }
            };
            panel.Children.Add(title);
            panel.Children.Add(_deleteBtnPopup);
            panel.Children.Add(_splitBtnPopup);
            return new Border
            {
                Background = new SolidColorBrush(Colors.Black) { Opacity = 0.78 },
                Padding = new Thickness(6),
                CornerRadius = new CornerRadius(4),
                Child = panel,
            };
        }

        private void UpdateVertexPopupPosition()
        {
            if (_viewport == null || _vertexPopup == null)
                return;

            // Show popup only when a vertex (handle) is selected; hide on mere hover
            if (!_editor.TryGetSelectedWorld(out var anchorWorld))
            {
                _vertexPopup.IsOpen = false;
                return;
            }

            var screen = _viewport.WorldToScreen(anchorWorld);
            _vertexPopup.PlacementTarget = _viewport;
            _vertexPopup.PlacementRect = new Rect(screen.X, screen.Y, 1, 1);
            // Flip placement near right/left edges of the viewport
            var bounds = _viewport.Bounds;
            var placeRight = screen.X < bounds.Width * 0.65;
            _vertexPopup.Placement = placeRight ? PlacementMode.Right : PlacementMode.Left;
            _vertexPopup.HorizontalOffset = 8 * (placeRight ? 1 : -1);
            // Slight upward offset
            _vertexPopup.VerticalOffset = -8;
            _vertexPopup.IsOpen = true;
        }

        private void UpdateContextUi()
        {
            var canDelete = _editor.SelectedHandleIndex >= 0 && _editor.Current != null && _editor.Current.Count > 3;
            var canSplit = _editor.HasHoveredEdge;
            if (_deleteBtnPanel != null) _deleteBtnPanel.IsEnabled = canDelete;
            if (_splitBtnPanel != null) _splitBtnPanel.IsEnabled = canSplit;
            if (_deleteBtnPopup != null) _deleteBtnPopup.IsEnabled = canDelete;
            if (_splitBtnPopup != null) _splitBtnPopup.IsEnabled = canSplit;
        }
    }
}
