﻿using Nucleus.Core;
using Nucleus.Types;
using Raylib_cs;
using System.Collections.Generic;

namespace Nucleus.UI.Elements
{
	public interface IMenuItem
	{
		public void Construct(Menu parent) { }
	}
	record MenuButton(string text, string? icon = null, Action? invoke = null) : IMenuItem;
	record MenuSubmenu(string text, string? icon = null, Func<Menu, bool>? invoke = null) : IMenuItem;
	record MenuSeparator() : IMenuItem;
	public class Menu : Panel
	{
		private List<IMenuItem> items = [];
		public void AddItem(IMenuItem item) {
			items.Add(item);
		}
		public void AddButton(string text, string? icon = null, Action? invoke = null) {
			items.Add(new MenuButton(text, icon, invoke));
		}
		public void AddSeparator() {
			items.Add(new MenuSeparator());
		}
		public void Open(Vector2F pos, bool popup = true, Menu? parent = null) {
			this.Position = pos;
			this.BorderSize = 1;

			this.BackgroundColor = new Color(20, 30, 45, 220);
			this.ForegroundColor = new Color(190, 195, 195, 114);

			var i = 0;
			this.Clipping = false;
			bool reverse = false;

			Menu? activeSubmenu = null;
			Element? lastHoveredPiece = null;

			var first = items.FirstOrDefault();
			var last = items.LastOrDefault();

			foreach (var item in items) {
				switch (item) {
					case MenuSeparator sep:
						if (item == first || item == last)
							continue;

						var s = this.Add<Panel>();
						s.Dock = Dock.Top;
						s.Size = new Types.Vector2F(0, 5);
						s.PaintOverride += S_PaintOverride;
						break;
					case MenuButton btn: {
							var b = this.Add<Button>();
							b.Dock = Dock.Top;
							b.Size = new Types.Vector2F(0, 28);
							b.Text = btn.text;
							b.AutoSize = false;
							b.TextPadding = new(12, 12);
							b.TextSize = 18;
							b.TextAlignment = Anchor.CenterLeft;
							b.BackgroundColor = new Raylib_cs.Color(0, 0, 0, 0);
							b.BorderSize = 0;
							b.MouseReleaseEvent += new MouseEventDelegate((e, fs, mb) => {
								btn.invoke?.Invoke();

								Menu ultimateMenu = this;
								while (true) {
									var parent = ultimateMenu.Parent;
									if (parent is not Menu parentMenu) break;
									ultimateMenu = parentMenu;
								}

								ultimateMenu?.Remove();
							});
							b.Thinking += (s) => {
								if (s.Hovered) {
									if (lastHoveredPiece != s) {
										activeSubmenu?.Remove();
									}
									lastHoveredPiece = s;
								}
							};
							b.Clipping = false;
							var mic = MathF.Max(items.Count, 8);

							b.PaintOverride += (self, width, height) => {
								float x = 0;
								var by = new Vector2F(x, 0);
								Graphics2D.OffsetDrawing(by);
								if (b.Hovered) {
									Graphics2D.SetDrawColor(70, 80, 90, 222);
									Graphics2D.DrawRectangle(0, 0, width, height);
								}
								b.Paint(width, height);
								Graphics2D.OffsetDrawing(-by);
							};
						}
						break;
					case MenuSubmenu submenu: {
							var b = this.Add<Button>();
							b.Dock = Dock.Top;
							b.Size = new Types.Vector2F(0, 28);
							b.Text = submenu.text;
							b.AutoSize = false;
							b.TextPadding = new(12, 12);
							b.TextSize = 18;
							b.TextAlignment = Anchor.CenterLeft;
							b.BackgroundColor = new Raylib_cs.Color(0, 0, 0, 0);
							b.BorderSize = 0;
							b.Thinking += (s) => {
								if (s.Hovered) {
									if (lastHoveredPiece != s) {
										activeSubmenu?.Remove();
										activeSubmenu = this.Add<Menu>();
										var shouldUse = submenu.invoke?.Invoke(activeSubmenu) ?? false;
										if (shouldUse) {
											activeSubmenu.Open(new Vector2F(s.RenderBounds.W + 8, s.GetGlobalPosition().Y - 7), false, this);
										}
										else {
											activeSubmenu.Remove();
											activeSubmenu = null;
										}
									}

									lastHoveredPiece = s;
								}
							};

							b.Clipping = false;
							var mic = MathF.Max(items.Count, 8);

							b.PaintOverride += (self, width, height) => {
								float x = 0;
								var by = new Vector2F(x, 0);
								Graphics2D.OffsetDrawing(by);
								if (b.Hovered) {
									Graphics2D.SetDrawColor(70, 80, 90, 222);
									Graphics2D.DrawRectangle(0, 0, width, height);
								}
								b.Paint(width, height);
								Graphics2D.OffsetDrawing(-by);
							};
						}
						break;
					default:
						item.Construct(this);
						break;
				}
				i++;
			}
			this.InvalidateLayout(true);
			float pX = 0;
			float pY = 0;
			foreach (var child in Children) {
				child.InvalidateLayout(true);
				var newP = child.RenderBounds.Pos + Graphics2D.GetTextSize(child.Text, child.Font, child.TextSize) + 16;
				if (newP.X > pX) pX = newP.X;
				if (newP.Y > pY) pY = newP.Y;

			}
			this.Size = new(pX + 12, pY - 4);
			var whereIsEnd = this.Position + this.Size + new Vector2F(4, 4);

			TextAlignment lr = TextAlignment.Left;
			TextAlignment tb = TextAlignment.Top;

			if (whereIsEnd.X > EngineCore.GetScreenBounds().W) {
				lr = TextAlignment.Right;
				reverse = true;
			}
			if (whereIsEnd.Y > EngineCore.GetScreenBounds().H) tb = TextAlignment.Bottom;

			this.Origin = TextAlignment.FromTextAlignment(lr, tb);
			if(popup)
				this.MakePopup();

			UI.OnElementClicked += UI_OnElementClicked;
		}

		private void S_PaintOverride(Element self, float width, float height) {
			var c = 145;
			Graphics2D.SetDrawColor(c, c, c);
			Graphics2D.DrawLine(8, height / 2, (width) - (8 * 2), height / 2);
		}

		private void UI_OnElementClicked(Element el, FrameState fs, Input.MouseButton mb) {
			if (this.Lifetime > 0.2f && (el == null || !el.IsIndirectChildOf(this))) {
				this.Remove();
				UI.OnElementClicked -= UI_OnElementClicked;
			}
		}
	}
}
