﻿using Nucleus.Core;
using Nucleus.ManagedMemory;
using Nucleus.Types;
using Nucleus.UI.Elements;
using Raylib_cs;

namespace Nucleus.ModelEditor
{
	public class VertexWeightWindow : Window {
		protected override void Initialize() {
			base.Initialize();
			Text = "Weights";
			Dock = Nucleus.UI.Dock.Right;
			Size = new(256);
			HideNonCloseButtons();
		}
	}
	public class VertexWeightOperator : DefaultOperator
	{
		/// <summary>
		/// Macro for checking if the last selected object is a mesh with weights.
		/// </summary>
		/// <returns></returns>
		public static bool IsMeshWithWeightsSelected() =>
			ModelEditor.Active.LastSelectedObject is EditorMeshAttachment meshAttachment
			&& meshAttachment.Weights != null
			&& meshAttachment.Weights.Count > 0;

		public override bool IsSelectable(IEditorType target) {
			if (target is EditorMeshAttachment) return true;

			return false;
		}
		public override void GizmoRender(EditorPanel editorPanel, IEditorType target) {
			return; // Placeholder
		}

		public override bool GizmoStartDragging(EditorPanel editorPanel, Vector2F mouseScreenStart, IEditorType? currentSelection, IEditorType? clicked) {
			return false; // Placeholder
		}

		public override void GizmoDrag(EditorPanel editorPanel, Vector2F mouseScreenStart, Vector2F mouseScreenNow, IEnumerable<IEditorType> targets) {
			return; // Placeholder
		}

		public override void GizmoEndDragging(EditorPanel editorPanel, Vector2F mouseScreenEnd, IEditorType target) {
			return; // Placeholder
		}

		public override bool GizmoClicked(EditorPanel editorPanel, IEditorType? target, Vector2F mouseScreenStart) {
			return true; // Placeholder
		}

		public override bool GizmoReleased(EditorPanel editorPanel, IEditorType? target, Vector2F mouseScreenStart) {
			return true; // Placeholder
		}

		public override void Activated() {
			return; // Placeholder
		}

		public override void Deactivated() {
			return; // Placeholder
		}
	}
}
