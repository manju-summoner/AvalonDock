/************************************************************************
   AvalonDock

   Copyright (C) 2007-2013 Xceed Software Inc.

   This program is provided to you under the terms of the Microsoft Public
   License (Ms-PL) as published at https://opensource.org/licenses/MS-PL
 ************************************************************************/

using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace AvalonDock.Controls
{
	/// <summary>
	/// Provides a panel that contains the TabItem Headers of the <see cref="LayoutDocumentPaneControl"/>.
	/// </summary>
	public class DocumentPaneTabPanel : Panel
	{
		#region Constructors

		/// <summary>
		/// Static constructor
		/// </summary>
		public DocumentPaneTabPanel()
		{
			this.FlowDirection = System.Windows.FlowDirection.LeftToRight;
		}

		#endregion Constructors

		#region Overrides

		protected override Size MeasureOverride(Size availableSize)
		{
			Size desideredSize = new Size();
			foreach (FrameworkElement child in Children)
			{
				child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
				desideredSize.Width += child.DesiredSize.Width;

				desideredSize.Height = Math.Max(desideredSize.Height, child.DesiredSize.Height);
			}

			return new Size(Math.Min(desideredSize.Width, availableSize.Width), desideredSize.Height);
		}

		/// <remarks>
		/// YMM4独自の変更。上流の実装（幅に収まらないタブをHiddenにする）に戻さないこと。
		/// YMM4のタブヘッダーは横スクロール可能なScrollViewerの中にあるため
		/// （フローティングウィンドウ内も含め、DocumentPaneControlStyleは常にこのテンプレートが適用される）、
		/// 収まらないタブもスクロールすれば到達でき、隠す必要がない。
		/// 隠す実装には、
		/// ・幅の判定に余裕がなく（正常時の超過量はちょうど0）、offsetの累積(ActualWidth+Margin)が
		/// 　タブ幅の合計をわずかでも超えると右端のタブが隠れてしまう
		/// 　（負のマージンやレイアウトの丸めが絡むと、両者はビット単位では一致しない）
		/// ・VisibilityのVisible⇔Hiddenの変更ではレイアウトが再実行されず、再実行されても
		/// 　MeasureOverrideがHiddenの子も合計に含めるため同じ判定になるので、
		/// 　一度隠すとタブが増減するまで空白のタブとして残り続ける
		/// という問題があった。
		/// </remarks>
		protected override Size ArrangeOverride(Size finalSize)
		{
			var visibleChildren = Children.Cast<UIElement>().Where(ch => ch.Visibility != System.Windows.Visibility.Collapsed);
			var offset = 0.0;

			foreach (TabItem doc in visibleChildren)
			{
				doc.Arrange(new Rect(offset, 0.0, doc.DesiredSize.Width, finalSize.Height));
				offset += doc.ActualWidth + doc.Margin.Left + doc.Margin.Right;
			}
			return finalSize;
		}

		protected override void OnMouseLeave(System.Windows.Input.MouseEventArgs e)
		{
			//if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed &&
			//    LayoutDocumentTabItem.IsDraggingItem())
			//{
			//    var contentModel = LayoutDocumentTabItem.GetDraggingItem().Model;
			//    var manager = contentModel.Root.Manager;
			//    LayoutDocumentTabItem.ResetDraggingItem();
			//    System.Diagnostics.Trace.WriteLine("OnMouseLeave()");

			//    manager.StartDraggingFloatingWindowForContent(contentModel);
			//}

			base.OnMouseLeave(e);
		}

		#endregion Overrides
	}
}