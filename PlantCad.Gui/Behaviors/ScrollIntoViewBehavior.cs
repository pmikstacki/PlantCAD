using System;
using System.Linq;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Controls.Models.TreeDataGrid;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Xaml.Interactivity;
using PlantCad.Gui.Services;

namespace PlantCad.Gui.Behaviors;

/// <summary>
/// Behavior that listens for an Action&lt;IndexPath&gt; event (default: "BringIntoViewRequested")
/// on the associated control's DataContext and scrolls the attached TreeDataGrid row into view.
/// </summary>
public class ScrollIntoViewBehavior : Behavior<TreeDataGrid>
{
    /// <summary>
    /// Name of an event on the DataContext to listen to. The event must be of type Action&lt;IndexPath&gt;.
    /// Defaults to "BringIntoViewRequested".
    /// </summary>
    public string EventName { get; set; } = "BringIntoViewRequested";

    private object? _currentDataContext;
    private EventInfo? _subscribedEvent;
    private Action<IndexPath>? _handler;
    private IndexPath? _pendingPath;

    protected override void OnAttached()
    {
        base.OnAttached();
        if (AssociatedObject == null)
        {
            return;
        }

        _currentDataContext = AssociatedObject.DataContext;
        TrySubscribe(_currentDataContext);
        AssociatedObject.DataContextChanged += OnDataContextChanged;
        AssociatedObject.AttachedToVisualTree += OnAttachedToVisualTree;
        AssociatedObject.LayoutUpdated += OnLayoutUpdated;
    }

    protected override void OnDetaching()
    {
        base.OnDetaching();
        if (AssociatedObject != null)
        {
            AssociatedObject.DataContextChanged -= OnDataContextChanged;
            AssociatedObject.AttachedToVisualTree -= OnAttachedToVisualTree;
            AssociatedObject.LayoutUpdated -= OnLayoutUpdated;
        }
        TryUnsubscribe(_currentDataContext);
        _currentDataContext = null;
    }

    private void OnAttachedToVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        // Try fulfill any pending request once the control is in the visual tree
        TryFulfillPendingRequest();
    }

    private void OnLayoutUpdated(object? sender, System.EventArgs e)
    {
        // Re-attempt on layout updates to fight timing with virtualization
        if (_pendingPath != null)
        {
            TryFulfillPendingRequest();
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        TryUnsubscribe(_currentDataContext);
        _currentDataContext = AssociatedObject?.DataContext;
        TrySubscribe(_currentDataContext);
    }

    private void TrySubscribe(object? dataContext)
    {
        try
        {
            if (dataContext == null)
            {
                return;
            }
            var eventName = EventName;
            var ei = dataContext
                .GetType()
                .GetEvent(eventName, BindingFlags.Public | BindingFlags.Instance);
            if (ei == null)
            {
                // No event found; log and return
                ServiceRegistry.LogsTool?.Append(
                    $"[{DateTime.Now:HH:mm:ss}] WARN ScrollIntoViewBehavior: Event '{eventName}' not found on DataContext {dataContext.GetType().Name}."
                );
                return;
            }

            if (ei.EventHandlerType != typeof(Action<IndexPath>))
            {
                ServiceRegistry.LogsTool?.Append(
                    $"[{DateTime.Now:HH:mm:ss}] WARN ScrollIntoViewBehavior: Event '{eventName}' has unsupported type {ei.EventHandlerType?.Name}, expected Action<IndexPath>."
                );
                return;
            }

            _handler = OnBringIntoView;
            ei.AddEventHandler(dataContext, _handler);
            _subscribedEvent = ei;
        }
        catch (Exception ex)
        {
            ServiceRegistry.LogsTool?.Append(
                $"[{DateTime.Now:HH:mm:ss}] ERROR ScrollIntoViewBehavior subscribe: {ex.Message}"
            );
            throw;
        }
    }

    private void TryUnsubscribe(object? dataContext)
    {
        try
        {
            if (dataContext == null || _subscribedEvent == null || _handler == null)
            {
                return;
            }
            _subscribedEvent.RemoveEventHandler(dataContext, _handler);
        }
        catch (Exception ex)
        {
            ServiceRegistry.LogsTool?.Append(
                $"[{DateTime.Now:HH:mm:ss}] WARN ScrollIntoViewBehavior unsubscribe: {ex.Message}"
            );
            throw;
        }
        finally
        {
            _subscribedEvent = null;
            _handler = null;
        }
    }

    private void OnBringIntoView(IndexPath path)
    {
        if (AssociatedObject == null)
        {
            return;
        }
        _pendingPath = path;
        // Defer to ensure Tree is realized and selection applied
        Dispatcher.UIThread.Post(TryFulfillPendingRequest, DispatcherPriority.Render);
    }

    private void TryFulfillPendingRequest()
    {
        try
        {
            var tree = AssociatedObject;
            var path = _pendingPath;
            if (tree == null || path == null)
            {
                return;
            }

            // Prefer a RowsPresenter implementation; try both public and non-public overloads
            var presenter = tree.GetVisualDescendants()
                .FirstOrDefault(d =>
                    d.GetType().Name.Contains("RowsPresenter", StringComparison.Ordinal)
                );
            if (presenter != null)
            {
                if (TryInvokeScrollIntoView(presenter, path.Value))
                {
                    _pendingPath = null;
                    return;
                }
                // As a fallback, bring the presenter into view
                (presenter as Control)?.BringIntoView();
                return;
            }

            // Try direct method on TreeDataGrid via reflection
            if (TryInvokeScrollIntoView(tree, path.Value))
            {
                _pendingPath = null;
                return;
            }

            // Final fallback: focus and BringIntoView the TreeDataGrid
            tree.Focus();
            tree.BringIntoView();
        }
        catch (Exception ex)
        {
            ServiceRegistry.LogsTool?.Append(
                $"[{DateTime.Now:HH:mm:ss}] WARN ScrollIntoViewBehavior TryFulfill: {ex.Message}"
            );
            throw;
        }
    }

    private static bool TryInvokeScrollIntoView(object target, IndexPath path)
    {
        var t = target.GetType();
        // Search for any instance method named ScrollIntoView that has first parameter compatible with IndexPath
        var methods = t.GetMethods(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            )
            .Where(m => string.Equals(m.Name, "ScrollIntoView", StringComparison.Ordinal))
            .ToArray();
        foreach (var mi in methods)
        {
            var ps = mi.GetParameters();
            if (
                ps.Length >= 1
                && ps[0]
                    .ParameterType.FullName?.Contains(
                        "IndexPath",
                        StringComparison.OrdinalIgnoreCase
                    ) == true
            )
            {
                var args = ps.Length switch
                {
                    1 => new object[] { path },
                    2 => new object[]
                    {
                        path,
                        ps[1].HasDefaultValue
                            ? ps[1].DefaultValue!
                            : GetDefault(ps[1].ParameterType)!,
                    },
                    _ => null,
                };
                if (args != null)
                {
                    mi.Invoke(target, args);
                    return true;
                }
            }
        }
        return false;
    }

    private static object? GetDefault(Type t) => t.IsValueType ? Activator.CreateInstance(t) : null;
}
