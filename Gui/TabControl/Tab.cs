﻿/*
Copyright (c) 2014, Lars Brubaker
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer.
2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR
ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

The views and conclusions contained in the software and documentation are those
of the authors and should not be interpreted as representing official policies,
either expressed or implied, of the FreeBSD Project.
*/

using System;
using System.Diagnostics;

namespace MatterHackers.Agg.UI
{
	public class SimpleTextTabWidget : Tab
	{
		private int fixedSize;

		public SimpleTextTabWidget(TabPage tabPageControledByTab, string internalTabName)
			: this(tabPageControledByTab, internalTabName, 12, RGBA_Bytes.DarkGray, RGBA_Bytes.White, RGBA_Bytes.Black, RGBA_Bytes.White)
		{
		}

		public SimpleTextTabWidget(TabPage tabPageControledByTab, string internalTabName, double pointSize,
			RGBA_Bytes selectedTextColor, RGBA_Bytes selectedBackgroundColor,
			RGBA_Bytes normalTextColor, RGBA_Bytes normalBackgroundColor, int fixedSize = 40, bool useUnderlineStyling = false)
			: base(internalTabName, new GuiWidget(), new GuiWidget(), new GuiWidget(), tabPageControledByTab)
		{
			this.Padding = new BorderDouble(5, 0);
			this.Margin = new BorderDouble(0, 0, 10, 0);
			this.fixedSize = fixedSize;
			this.UseUnderlineStyling = useUnderlineStyling;

			AddText(tabPageControledByTab.Text, selectedWidget, selectedTextColor, selectedBackgroundColor, pointSize, true);
			AddText(tabPageControledByTab.Text, normalWidget, normalTextColor, normalBackgroundColor, pointSize, false);

			tabPageControledByTab.TextChanged += new EventHandler(tabPageControledByTab_TextChanged);

			SetBoundsToEncloseChildren();
		}

		public bool UseUnderlineStyling { get; set; } = false;

		private void tabPageControledByTab_TextChanged(object sender, EventArgs e)
		{
			normalWidget.Children[0].Text = ((GuiWidget)sender).Text;
			normalWidget.SetBoundsToEncloseChildren();
			selectedWidget.Children[0].Text = ((GuiWidget)sender).Text;
			selectedWidget.SetBoundsToEncloseChildren();
			SetBoundsToEncloseChildren();
		}

		public TextWidget tabTitle;

		private void AddText(string tabText, GuiWidget widgetState, RGBA_Bytes textColor, RGBA_Bytes backgroundColor, double pointSize, bool isActive)
		{
			tabTitle = new TextWidget(tabText, pointSize: pointSize, textColor: textColor)
			{
				VAnchor = VAnchor.Center,
			};
			tabTitle.AutoExpandBoundsToText = true;
			widgetState.AddChild(tabTitle);
			widgetState.Selectable = false;
			widgetState.BackgroundColor = backgroundColor;

			EnforceSizingAdornActive(widgetState, isActive, this.UseUnderlineStyling, this.fixedSize);
		}
	}

	public abstract class Tab : GuiWidget
	{
		public static int UnderlineHeight { get; set; } = 2;

		private RGBA_Bytes backgroundColor = new RGBA_Bytes(230, 230, 230);

		protected GuiWidget normalWidget;
		protected GuiWidget hoverWidget;
		protected GuiWidget selectedWidget;

		public event EventHandler Selected;

		public Tab(string tabName, GuiWidget normalWidget, GuiWidget hoverWidget, GuiWidget pressedWidget,
			TabPage tabPage)
		{
			this.Name = tabName;
			this.normalWidget = normalWidget;
			this.hoverWidget = hoverWidget;
			this.selectedWidget = pressedWidget;
			this.Padding = new BorderDouble(5, 3, 20, 3);
			this.TabPage = tabPage;

			AddChild(normalWidget);
			AddChild(hoverWidget);
			AddChild(pressedWidget);

			hoverWidget.Visible = false;
			pressedWidget.Visible = false;
			
			SetBoundsToEncloseChildren();
		}

		public override void OnParentChanged(EventArgs e)
		{
			TabBarContaningTab.TabIndexChanged += SelectionChanged;
			base.OnParentChanged(e);
		}

		public virtual void OnSelected(EventArgs e)
		{
			Selected?.Invoke(this, e);
		}

		public override void OnMouseDown(MouseEventArgs mouseEvent)
		{
			OnSelected(mouseEvent);
			base.OnClick(mouseEvent);
		}

		private void SelectionChanged(object sender, EventArgs e)
		{
			if (TabBarContaningTab != null)
			{
				bool selected = TabPage == TabBarContaningTab.GetActivePage();

				if (selected)
				{
					normalWidget.Visible = false;
					hoverWidget.Visible = false;
					selectedWidget.Visible = true;
				}
				else
				{
					normalWidget.Visible = true;
					hoverWidget.Visible = false;
					selectedWidget.Visible = false;
				}
			}
		}

		public TabBar TabBarContaningTab => Parent as TabBar;

		public TabPage TabPage { get; }

		protected static void EnforceSizingAdornActive(GuiWidget widgetState, bool isActive, bool useUnderlineStyle, int controlHeight = 40, int controlMargin = 0)
		{
			widgetState.Height = controlHeight;
			widgetState.Margin = controlMargin;

			if (isActive && useUnderlineStyle)
			{
				// Adorn the active tab with a underline bar
				widgetState.AddChild(new GuiWidget()
				{
					HAnchor = HAnchor.Stretch,
					Height = UnderlineHeight,
					BackgroundColor = ActiveTheme.Instance.PrimaryAccentColor,
					VAnchor = VAnchor.Bottom
				});
			}

			RectangleDouble childrenBounds = widgetState.GetMinimumBoundsToEncloseChildren();
			widgetState.LocalBounds = new RectangleDouble(childrenBounds.Left, widgetState.LocalBounds.Bottom, childrenBounds.Right, widgetState.LocalBounds.Top);
		}
	}
}