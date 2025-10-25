/************************************************************************
   AvalonDock

   Copyright (C) 2007-2013 Xceed Software Inc.

   This program is provided to you under the terms of the Microsoft Public
   License (Ms-PL) as published at https://opensource.org/licenses/MS-PL
 ************************************************************************/

using AvalonDock.Layout;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace AvalonDock.Controls
{
	/// <summary>
	/// Implements a control that contains a <see cref="LayoutAnchorableControl"/>.
	/// The <see cref="LayoutAutoHideWindowControl"/> pops out of a side panel
	/// when the user clicks on a <see cref="LayoutAnchorControl"/> of a particular anchored item.
	/// </summary>
	public class LayoutAutoHideWindowControl : HwndHost, ILayoutControl
	{
		#region fields

		internal LayoutAnchorableControl _internalHost = null;

		private LayoutAnchorControl _anchor;
		private LayoutAnchorable _model;
		private HwndSource _internalHwndSource = null;
		private IntPtr parentWindowHandle;
		private readonly ContentPresenter _internalHostPresenter = new ContentPresenter();
		private Grid _internalGrid = null;
		private Border _border = null;
		private AnchorSide _side;
		private LayoutGridResizerControl _resizer = null;
		private DockingManager _manager;
		private List<FrameworkElement> _sizeChangedListeningControls;
		private SizeChangedEventHandler _sizeChangedHandler;
		private Point _prevPoint;

		#endregion fields

		#region Constructors

		static LayoutAutoHideWindowControl()
		{
			DefaultStyleKeyProperty.OverrideMetadata(typeof(LayoutAutoHideWindowControl), new FrameworkPropertyMetadata(typeof(LayoutAutoHideWindowControl)));
			FocusableProperty.OverrideMetadata(typeof(LayoutAutoHideWindowControl), new FrameworkPropertyMetadata(true));
			Control.IsTabStopProperty.OverrideMetadata(typeof(LayoutAutoHideWindowControl), new FrameworkPropertyMetadata(true));
			VisibilityProperty.OverrideMetadata(typeof(LayoutAutoHideWindowControl), new FrameworkPropertyMetadata(Visibility.Hidden));
		}

		internal LayoutAutoHideWindowControl()
		{
			_sizeChangedHandler = ViewboxZoomChanged;
		}

		#endregion Constructors

		#region Properties

		#region AnchorableStyle

		/// <summary><see cref="AnchorableStyle"/> dependency property.</summary>
		public static readonly DependencyProperty AnchorableStyleProperty = DependencyProperty.Register(nameof(AnchorableStyle), typeof(Style), typeof(LayoutAutoHideWindowControl),
				new FrameworkPropertyMetadata(null));

		/// <summary>Gets/sets the style to apply to the <see cref="LayoutAnchorableControl"/> hosted in this auto hide window.</summary>
		[Bindable(true), Description("Gets/sets the style to apply to the LayoutAnchorableControl hosted in this auto hide window."), Category("Style")]
		public Style AnchorableStyle
		{
			get => (Style)GetValue(AnchorableStyleProperty);
			set => SetValue(AnchorableStyleProperty, value);
		}

		#endregion AnchorableStyle

		#region Background

		/// <summary><see cref="Background"/> dependency property.</summary>
		public static readonly DependencyProperty BackgroundProperty = DependencyProperty.Register(nameof(Background), typeof(Brush), typeof(LayoutAutoHideWindowControl),
				new FrameworkPropertyMetadata(null));

		/// <summary>Gets/sets the background brush of the autohide childwindow.</summary>
		[Bindable(true), Description("Gets/sets the background brush of the autohide childwindow."), Category("Other")]
		public Brush Background
		{
			get => (Brush)GetValue(BackgroundProperty);
			set => SetValue(BackgroundProperty, value);
		}

		#endregion Background

		#region BorderBrush
		/// <summary><see cref="BorderBrush"/> dependency property.</summary>
		public static readonly DependencyProperty BorderBrushProperty = DependencyProperty.Register(nameof(BorderBrush), typeof(Brush), typeof(LayoutAutoHideWindowControl),
				new FrameworkPropertyMetadata(null));

		/// <summary>Gets/sets the border brush of the autohide childwindow.</summary>
		[Bindable(true), Description("Gets/sets the border brush of the autohide childwindow."), Category("Other")]
		public Brush BorderBrush
		{
			get => (Brush)GetValue(BorderBrushProperty);
			set => SetValue(BorderBrushProperty, value);
		}

		#endregion BorderBrush

		#region BorderThickness
		/// <summary><see cref="BorderThickness"/> dependency property.</summary>
		public static readonly DependencyProperty BorderThicknessProperty = DependencyProperty.Register(nameof(BorderThickness), typeof(Thickness), typeof(LayoutAutoHideWindowControl),
				new FrameworkPropertyMetadata(new Thickness(1)));

		/// <summary>Gets/sets the border thickness of the autohide childwindow.</summary>
		[Bindable(true), Description("Gets/sets the border thickness of the autohide childwindow."), Category("Other")]
		public Thickness BorderThickness
		{
			get => (Thickness)GetValue(BorderThicknessProperty);
			set => SetValue(BorderThicknessProperty, value);
		}
		#endregion BorderThickness

		public ILayoutElement Model => _model;

		/// <summary>Resizer</summary>
		internal bool IsResizing { get; private set; }

		public AnchorSide Side => _side;

		#endregion Properties

		#region Internal Methods

		internal void Show(LayoutAnchorControl anchor)
		{
			if (_model != null) throw new InvalidOperationException();

			_anchor = anchor;
			_model = anchor.Model as LayoutAnchorable;
			_model.IsSelected = true;
			_model.IsActive = true;
			_side = (anchor.Model.Parent.Parent as LayoutAnchorSide).Side;
			_manager = _model.Root.Manager;
			CreateInternalGrid();
			_model.PropertyChanged += _model_PropertyChanged;
			SetLayoutTransform();
			StartListeningToViewboxZoomChange();
			Visibility = Visibility.Visible;
			InvalidateMeasure();
			UpdateWindowPos();
			Win32Helper.BringWindowToTop(_internalHwndSource.Handle);
		}

		internal void Hide()
		{
			StopListeningToViewboxZoomChange();

			if (_model == null) return;
			_model.PropertyChanged -= _model_PropertyChanged;
			_model.IsSelected = false;
			_model.IsActive = false;
			RemoveInternalGrid();
			_anchor = null;
			_model = null;
			_manager = null;
			Visibility = Visibility.Hidden;
		}
		#endregion Internal Methods

		#region Overrides

		/// <inheritdoc />
		protected override HandleRef BuildWindowCore(HandleRef hwndParent)
		{
			parentWindowHandle = hwndParent.Handle;
			_internalHwndSource = new HwndSource(new HwndSourceParameters
			{
				ParentWindow = hwndParent.Handle,
				WindowStyle = Win32Helper.WS_CHILD | Win32Helper.WS_VISIBLE | Win32Helper.WS_CLIPSIBLINGS | Win32Helper.WS_CLIPCHILDREN,
				Width = 0,
				Height = 0,
			})
			{ RootVisual = _internalHostPresenter };
			AutomationProperties.SetName(_internalHostPresenter, "InternalWindowHost");
			AddLogicalChild(_internalHostPresenter);
			Win32Helper.BringWindowToTop(_internalHwndSource.Handle);
			return new HandleRef(this, _internalHwndSource.Handle);
		}

		/// <inheritdoc />
		protected override void DestroyWindowCore(HandleRef hwnd)
		{
			if (_internalHwndSource == null) return;
			_internalHwndSource.Dispose();
			_internalHwndSource = null;
		}

		/// <inheritdoc />
		protected override bool HasFocusWithinCore() => false;

		/// <inheritdoc />
		protected override System.Collections.IEnumerator LogicalChildren => _internalHostPresenter == null ? new UIElement[] { }.GetEnumerator() : new UIElement[] { _internalHostPresenter }.GetEnumerator();

		/// <inheritdoc />
		protected override Size MeasureOverride(Size constraint)
		{
			if (_internalHostPresenter == null) return base.MeasureOverride(constraint);
			_internalHostPresenter.Measure(constraint);
			//return base.MeasureOverride(constraint);
			return _internalHostPresenter.DesiredSize;
		}

		/// <inheritdoc />
		protected override Size ArrangeOverride(Size finalSize)
		{
			if (_internalHostPresenter == null) return base.ArrangeOverride(finalSize);
			_internalHostPresenter.Arrange(new Rect(finalSize));
			return base.ArrangeOverride(finalSize);// new Size(_internalHostPresenter.ActualWidth, _internalHostPresenter.ActualHeight);
		}

		#endregion Overrides

		#region Private Methods

		private void _model_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
		{
			if (e.PropertyName != nameof(LayoutAnchorable.IsAutoHidden)) return;
			if (!_model.IsAutoHidden) _manager.HideAutoHideWindow(_anchor);
		}

		private Transform ChildLayoutTransform
		{
			get
			{
				var viewboxes = _manager.GetParents().OfType<Viewbox>().ToList();

				if (viewboxes.Any())
				{
					if (_manager.TransformToAncestor(viewboxes[viewboxes.Count - 1]) is Transform transform)
					{
						if (!transform.Value.IsIdentity)
						{
							var origin = transform.Transform(new Point());

							var newTransformGroup = new TransformGroup();
							newTransformGroup.Children.Add(transform);
							newTransformGroup.Children.Add(new TranslateTransform(-origin.X, -origin.Y));
							newTransformGroup.Children.Add(CustomLayoutTransform);
							return newTransformGroup;
						}
					}
				}

				return CustomLayoutTransform;
			}
		}

		public Transform CustomLayoutTransform
		{
			get { return (Transform)GetValue(CustomLayoutTransformProperty); }
			set { SetValue(CustomLayoutTransformProperty, value); }
		}
		public static readonly DependencyProperty CustomLayoutTransformProperty =
			DependencyProperty.Register(nameof(CustomLayoutTransform), typeof(Transform), typeof(LayoutAutoHideWindowControl), new PropertyMetadata(Transform.Identity));

		private void SetLayoutTransform()
		{
			// We refresh this each time either:
			// 1) The window is created.
			// 2) An ancestor Viewbox changes its zoom (the Viewbox or its child changes size)
			// We would also want to refresh when the visual tree changes such that an ancestor Viewbox is added, removed, or changed. However, this is completely unnecessary
			// because the LayoutAutoHideWindowControl closes if a visual ancestor is changed: DockingManager.Unloaded handler calls _autoHideWindowManager?.HideAutoWindow()
			if (ChildLayoutTransform is Transform transform &&( _internalHostPresenter.LayoutTransform.Value != transform.Value || LayoutTransform.Value != ((Transform)transform.Inverse).Value))
			{ 
				LayoutTransform = (Transform)transform.Inverse;
				_internalHostPresenter.LayoutTransform = transform;
			}
		}
		private void StartListeningToViewboxZoomChange()
		{
			StopListeningToViewboxZoomChange();
			_sizeChangedListeningControls = _manager.GetParents().OfType<Viewbox>().SelectMany(x => new[] { x, x.Child }).OfType<FrameworkElement>().Distinct().ToList();
			_sizeChangedListeningControls.ForEach(x => x.SizeChanged += _sizeChangedHandler);
		}
		private void StopListeningToViewboxZoomChange()
		{
			_sizeChangedListeningControls?.ForEach(x => x.SizeChanged -= _sizeChangedHandler);
			_sizeChangedListeningControls?.Clear();
		}
		private void ViewboxZoomChanged(object sender, SizeChangedEventArgs e)
		{
			SetLayoutTransform();
		}

		private void CreateInternalGrid()
		{
			_internalGrid = new Grid { FlowDirection = FlowDirection.LeftToRight };
			_internalGrid.SetBinding(Panel.BackgroundProperty, new Binding(nameof(Grid.Background)) { Source = this });

			_border = new Border();
			_border.SetBinding(Border.BorderBrushProperty, new Binding(nameof(BorderBrush)) { Source = this });
			_border.SetBinding(Border.BorderThicknessProperty, new Binding(nameof(BorderThickness)) { Source = this });
			Grid.SetColumnSpan(_border, 2);
			Grid.SetRowSpan(_border, 2);
			Panel.SetZIndex(_border, 1);
			_internalGrid.Children.Add(_border);

			_internalHost = new LayoutAnchorableControl { Model = _model, Style = AnchorableStyle };
			_internalHost.SetBinding(FlowDirectionProperty, new Binding("Model.Root.Manager.FlowDirection") { Source = this });

			KeyboardNavigation.SetTabNavigation(_internalGrid, KeyboardNavigationMode.Cycle);
			_resizer = new LayoutGridResizerControl();

			_resizer.DragStarted += OnResizerDragStarted;
			_resizer.DragDelta += OnResizerDragDelta;

			switch (_side)
			{
				case AnchorSide.Right:
					_internalGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(_manager.GridSplitterWidth) });
					_internalGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = _model.AutoHideWidth == 0.0 ? new GridLength(_model.AutoHideMinWidth) : new GridLength(_model.AutoHideWidth, GridUnitType.Pixel) });
					Grid.SetColumn(_resizer, 0);
					Grid.SetColumn(_internalHost, 1);
					_resizer.Cursor = Cursors.SizeWE;
					HorizontalAlignment = HorizontalAlignment.Right;
					VerticalAlignment = VerticalAlignment.Stretch;
					break;

				case AnchorSide.Left:
					_internalGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = _model.AutoHideWidth == 0.0 ? new GridLength(_model.AutoHideMinWidth) : new GridLength(_model.AutoHideWidth, GridUnitType.Pixel) });
					_internalGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(_manager.GridSplitterWidth) });
					Grid.SetColumn(_internalHost, 0);
					Grid.SetColumn(_resizer, 1);
					_resizer.Cursor = Cursors.SizeWE;
					HorizontalAlignment = HorizontalAlignment.Left;
					VerticalAlignment = VerticalAlignment.Stretch;
					break;

				case AnchorSide.Top:
					_internalGrid.RowDefinitions.Add(new RowDefinition { Height = _model.AutoHideHeight == 0.0 ? new GridLength(_model.AutoHideMinHeight) : new GridLength(_model.AutoHideHeight, GridUnitType.Pixel), });
					_internalGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(_manager.GridSplitterHeight) });
					Grid.SetRow(_internalHost, 0);
					Grid.SetRow(_resizer, 1);
					_resizer.Cursor = Cursors.SizeNS;
					VerticalAlignment = VerticalAlignment.Top;
					HorizontalAlignment = HorizontalAlignment.Stretch;
					break;

				case AnchorSide.Bottom:
					_internalGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(_manager.GridSplitterHeight) });
					_internalGrid.RowDefinitions.Add(new RowDefinition { Height = _model.AutoHideHeight == 0.0 ? new GridLength(_model.AutoHideMinHeight) : new GridLength(_model.AutoHideHeight, GridUnitType.Pixel), });
					Grid.SetRow(_resizer, 0);
					Grid.SetRow(_internalHost, 1);
					_resizer.Cursor = Cursors.SizeNS;
					VerticalAlignment = VerticalAlignment.Bottom;
					HorizontalAlignment = HorizontalAlignment.Stretch;
					break;
			}
			_internalGrid.Children.Add(_resizer);
			_internalGrid.Children.Add(_internalHost);
			_internalHostPresenter.Content = _internalGrid;
		}

		private void RemoveInternalGrid()
		{
			_resizer.DragStarted -= OnResizerDragStarted;
			_resizer.DragDelta -= OnResizerDragDelta;
			_internalHostPresenter.Content = null;
		}

		private void OnResizerDragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e)
		{
			IsResizing = true;
			var window = Window.GetWindow(this);
			_prevPoint = Mouse.GetPosition(window);
		}

		private void OnResizerDragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
		{
			var window = Window.GetWindow(this);
			var currentPoint = Mouse.GetPosition(window);
			var dv = currentPoint - _prevPoint;
			var tdv = this.LayoutTransform.Transform(new Point(dv.X, dv.Y));

			double delta;
			if (_side == AnchorSide.Right || _side == AnchorSide.Left)
				delta = tdv.X;
			else
				delta = tdv.Y;
			_prevPoint = currentPoint;

			switch (_side)
			{
				case AnchorSide.Right:
					{
						if (_model.AutoHideWidth == 0.0) _model.AutoHideWidth = _internalHost.ActualWidth - delta;
						else _model.AutoHideWidth -= delta;
						_internalGrid.ColumnDefinitions[1].Width = new GridLength(_model.AutoHideWidth, GridUnitType.Pixel);
						break;
					}
				case AnchorSide.Left:
					{
						if (_model.AutoHideWidth == 0.0) _model.AutoHideWidth = _internalHost.ActualWidth + delta;
						else _model.AutoHideWidth += delta;
						_internalGrid.ColumnDefinitions[0].Width = new GridLength(_model.AutoHideWidth, GridUnitType.Pixel);
						break;
					}
				case AnchorSide.Top:
					{
						if (_model.AutoHideHeight == 0.0) _model.AutoHideHeight = _internalHost.ActualHeight + delta;
						else _model.AutoHideHeight += delta;
						_internalGrid.RowDefinitions[0].Height = new GridLength(_model.AutoHideHeight, GridUnitType.Pixel);
						break;
					}
				case AnchorSide.Bottom:
					{
						if (_model.AutoHideHeight == 0.0) _model.AutoHideHeight = _internalHost.ActualHeight - delta;
						else _model.AutoHideHeight -= delta;
						_internalGrid.RowDefinitions[1].Height = new GridLength(_model.AutoHideHeight, GridUnitType.Pixel);
						break;
					}
			}
			IsResizing = false;
			InvalidateMeasure();


		}

		#endregion Private Methods
	}
}
