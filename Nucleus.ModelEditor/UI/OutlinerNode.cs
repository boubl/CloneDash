﻿using Nucleus.Core;
using Nucleus.Extensions;
using Nucleus.Types;
using Nucleus.UI;
using Raylib_cs;
using MouseButton = Nucleus.Input.MouseButton;

namespace Nucleus.ModelEditor
{
	public class OutlinerNode : Button, IContainsOutlinerNodes
	{
		Button Visibility;
		Button Keyframe;
		Button Expander;
		Panel Image;

		private WeakReference? __represents;

		public IEditorType? GetRepresentingObject() => (IEditorType?)(__represents == null ? null : __represents.Target == null ? null : __represents.Target);
		public T? GetRepresentingObject<T>() where T : class => __represents == null ? null : __represents.Target == null ? null : (T)__represents.Target;
		public void SetRepresentingObject(IEditorType obj) {
			__represents = new(obj);
			Visibility.Enabled = obj.CanHide();
		}


		private void SELECTABLECHANGED() {
			TextColor = GetSelectable() ? Color.White : Color.Gray;
		}

		private bool _selectable = true;
		private bool? _selectableOverride = null;
		public bool Selectable {
			get => _selectable;
			set { _selectable = value; SELECTABLECHANGED(); }
		}

		/// <summary>
		/// Set by the editor; you probably want <see cref="Selectable"/>.
		/// </summary>
		public bool? SelectableOverride {
			get => _selectableOverride;
			set { _selectableOverride = value; SELECTABLECHANGED(); }
		}

		public bool GetSelectable() => _selectableOverride ?? _selectable;

		public int Layer = 0;
		public List<OutlinerNode> Children = [];

		public delegate void ChangeChildOrderD(OutlinerNode node, List<OutlinerNode> childrenList);
		public event ChangeChildOrderD? ChangeChildOrder;
		public IEnumerable<OutlinerNode> GetChildNodesInOrder() {
			ChangeChildOrder?.Invoke(this, Children);
			return Children;
		}

		private OutlinerNode? parentNode;
		public OutlinerNode? ParentNode {
			get => parentNode;
			set {
				if (parentNode != value) {
					if (IValidatable.IsValid(parentNode))
						parentNode.Children.Remove(this);

					if (IValidatable.IsValid(value))
						value.Children.Add(this);

					parentNode = value;
				}
			}
		}

		public void ClearChildNodes() {
			while (Children.Count > 0)
				Children.RemoveAt(0);

			Outliner.InvalidateLayout();
			Outliner.InvalidateChildren();
		}

		public void InvalidateNode() {
			Outliner.InvalidateLayout();
			Outliner.InvalidateChildren();
		}

		public OutlinerPanel Outliner;

		private bool __expanded = true;
		public bool Expanded {
			get => __expanded;
			set {
				if (value == __expanded) return;

				__expanded = value;
				foreach (var child in Children) {
					child.EngineDisabled = !__expanded;
				}
			}
		}

		public ManagedMemory.Texture? ImageTexture {
			get => Image.Image;
			set => Image.Image = value;
		}
		public new Raylib_cs.Color ImageColor {
			get => Image.ImageColor ?? Image.TextColor;
			set => Image.ImageColor = value;
		}

		protected override void Initialize() {
			base.Initialize();

			Add(out Visibility);
			Add(out Keyframe);
			Add(out Expander);
			Add(out Image);

			this.Size = new(24);

			Visibility.Position = new(-7, 2);
			Keyframe.Position = new(23 - 7, 2);
			Expander.Position = new(46 - 7, 2);
			Image.Position = new(56, 2);

			Visibility.Size = new(23);
			Keyframe.Size = new(23);
			Expander.Size = new(23);
			Image.Size = new(16);

			Image.ImageColor = Raylib_cs.Color.White;

			BorderSize = 0;
			DockMargin = RectangleF.TLRB(0, 2, 2, 0);

			Image.DrawPanelBackground = false;
			Image.ImageOrientation = ImageOrientation.Fit;

			Visibility.PaintOverride += Visibility_PaintOverride;
			Keyframe.PaintOverride += Keyframe_PaintOverride;
			Expander.PaintOverride += Expander_PaintOverride;

			Visibility.MouseClickEvent += Visibility_MouseClickEvent;
			Keyframe.MouseClickEvent += Keyframe_MouseClickEvent; ;

			Expander.MouseReleaseEvent += Expander_MouseReleaseEvent;

			// we want text and label to passthru
			Image.OnHoverTest += Passthru;

			Keyframe.Visible = Keyframe.Enabled = ModelEditor.Active.AnimationMode;
			ModelEditor.Active.SetupAnimateModeChanged += (_, animateMode) => {
				Keyframe.Visible = Keyframe.Enabled = animateMode;
			};

			Dock = Dock.Top;
		}

		private void Keyframe_MouseClickEvent(Element self, FrameState state, MouseButton button) {
			IEditorType? editorItem = GetRepresentingObject();
			if (editorItem == null) return;

			if (
				button == MouseButton.Mouse1 
				&& ModelEditor.Active.CanInsertKeyframes() 
				&& editorItem.CanKeyframe() 
				&& editorItem.GetKeyframeParameters(out var target, out var prop, out var index)
			)
				ModelEditor.Active.File.InsertKeyframe(target, prop, index);
			else // Redirect the click event to expander
				Expander_MouseReleaseEvent(self, state, button);
		}

		private void Visibility_MouseClickEvent(Element self, FrameState state, MouseButton button) {
			IEditorType? editorItem = GetRepresentingObject();
			if (editorItem == null) return;

			if (editorItem.GetVisible())
				ModelEditor.Active.File.HideEditorItem(editorItem);
			else
				ModelEditor.Active.File.ShowEditorItem(editorItem);
		}

		private void __setExpandedRecursive(bool state) {
			foreach (var childNode in Children)
				childNode.__setExpandedRecursive(state);

			Expanded = state;
		}

		private void Expander_MouseReleaseEvent(Element self, FrameState state, MouseButton button) {
			if (button == MouseButton.Mouse2)
				__setExpandedRecursive(!Expanded);
			else
				Expanded = !Expanded;
			Outliner.InvalidateLayout();
			Outliner.InvalidateChildren();
		}

		public override void OnRemoval() {
			base.OnRemoval();
			foreach (var child in Children.ToArray()) {
				if (IValidatable.IsValid(child))
					child.Remove();
			}
			Children.Clear();
			if (ParentNode == null) Outliner.RootNodes.Remove(this);
			else ParentNode.Children.Remove(this);

			Outliner.InvalidateLayout();
			Outliner.InvalidateChildren();
		}

		protected override void PerformLayout(float width, float height) {
			Visibility.Size = new(Visibility.Size.X, height);
			Keyframe.Size = new(Keyframe.Size.X, height);
			base.PerformLayout(width, height);
			Expander.Position = new(40, 2);
			Expander.Size = new(23 + (Layer * 16), height);
			Image.Size = new(Image.Size.X, height);

			Image.Position = new(Expander.Position.X + Expander.Size.X, 0);
			Expander.Visible = Children.Count > 0;

			TextAlignment = Anchor.CenterLeft;
			TextPadding = new(Image.Position.X + Image.Size.X + 8, 0);
		}

		public override void TextChanged(string oldText, string newText) {
			base.TextChanged(oldText, newText);
			// Because a lot of things are text-dependent!
			// ie. alphabetical sorting
			Outliner.InvalidateLayout();
			Outliner.InvalidateChildren();
		}

		public override void Paint(float width, float height) {
			BackgroundColor = (GetRepresentingObject()?.Selected ?? false) ? DefaultBackgroundColor.Adjust(0, 0.5, 2.4) : DefaultBackgroundColor;
			base.Paint(width, height);
			if (Layer > 0 && ParentNode != null) {
				int count = ParentNode.Children.Count;
				bool last = count == 0 ? true : (count == 1 || ParentNode.Children[count - 1] == this);
				if (Expanded && Children.Count > 0)
					last = false;
				Graphics2D.SetDrawColor(220, 220, 220, 60);
				var x = (TextPadding.X - 52) + (Layer * 0);
				Graphics2D.DrawLine(x, 0, x, last ? height / 2 : height);
				Graphics2D.DrawLine(x, height / 2, x + 16, height / 2);

				if (Layer > 1) {
					for (int i = Layer - (1); i >= 1; i--) {
						Graphics2D.DrawLine(x - (i * 16), 0, x - (i * 16), height);
					}
				}
			}
		}
		private void Expander_PaintOverride(Element self, float width, float height) {
			var c = self.Depressed ? 100 : self.Hovered ? 220 : 170;
			Graphics2D.SetDrawColor(c, c, c);
			Graphics2D.SetTexture(UI.Level.Textures.LoadTextureFromFile(Expanded ? "models/expanded.png" : "models/collapsed.png"));
			var s = 16;
			Graphics2D.DrawImage(new(width - 19, (height / 2) - (s / 2) - 1), new(s), new(0.5f));
		}

		private void Keyframe_PaintOverride(Element self, float width, float height) {
			IEditorType? editorItem = GetRepresentingObject();
			
			if (editorItem == null) return;
			if (!ModelEditor.Active.CanInsertKeyframes()) return;
			if (!editorItem.CanKeyframe()) return;
			if (!editorItem.GetKeyframeParameters(out var target, out var property, out var index)) return;

			// Search for the timeline, set up KeyframeState enum for rendering
			var anim = ModelEditor.Active.File.ActiveAnimation;
			if (anim == null) return;

			var timeline = anim.SearchTimelineByProperty(target, property, index, false);
			KeyframeState state = timeline?.KeyframedAt(ModelEditor.Active.File.Timeline.Frame) ?? KeyframeState.NotKeyframed;

			Color color = state switch {
				KeyframeState.NotKeyframed => KeyframeButton.KEYFRAME_COLOR_NOT_KEYFRAMED,
				KeyframeState.PendingKeyframe => KeyframeButton.KEYFRAME_COLOR_PENDING_KEYFRAME,
				KeyframeState.Keyframed => KeyframeButton.KEYFRAME_COLOR_ACTIVE_KEYFRAME
			};

			color = MixColorBasedOnMouseState(self, color, new(0, .6f, 1.2f, 1), new(0, 1, .5f, 1));

			Graphics2D.SetDrawColor(color);
			Graphics2D.SetTexture(Textures.LoadTextureFromFile("models/keyframe.png"));
			Graphics2D.DrawImage(RectangleF.FromPosAndSize(new(2), new(height - 4)));
		}

		private void Visibility_PaintOverride(Element self, float width, float height) {
			IEditorType? editorItem = GetRepresentingObject();
			if (editorItem == null) return;


			if (editorItem is EditorAttachment attachment) {
				var visColor = attachment.Slot.GetActiveAttachment() == attachment ? 185 : 80;
				var c = self.Depressed ? (visColor / 2) : self.Hovered ? (visColor + 35) : visColor;
				Graphics2D.SetDrawColor(c, c, c);
				Graphics2D.SetTexture(UI.Level.Textures.LoadTextureFromFile("models/paperclip.png"));
				Graphics2D.DrawImage(RectangleF.XYWH(4, 4, width - 8, height - 8), new(0, 0));
			}
			else {
				var visColor = editorItem.GetVisible() ? 185 : 125;
				var c = self.Depressed ? (visColor / 2) : self.Hovered ? (visColor + 35) : visColor;
				Graphics2D.SetDrawColor(c, c, c);

				Graphics2D.DrawCircle(new(width / 2f, (height / 2f) - 2), width / 7);
			}
		}

		protected override void OnThink(FrameState frameState) {
			base.OnThink(frameState);
		}

		public OutlinerNode AddNode(string text, string? icon = null) {
			OutlinerNode node = OutlinerPanel.SetupNode(Outliner, Layer + 1, this, text, icon);
			node.EngineDisabled = !Expanded;

			return node;
		}
	}
}
