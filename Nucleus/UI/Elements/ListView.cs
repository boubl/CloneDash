﻿using Nucleus.Core;
using Nucleus.Types;
using Raylib_cs;

namespace Nucleus.UI
{
    public class ListView : ScrollPanel
    {
        public Element? LastSelectedElement { get; private set; } = null;
        public HashSet<Element> SelectedElements { get; private set; } = [];
        protected override void Initialize() {
            base.Initialize();
            DockPadding = RectangleF.TLRB(2);
        }
        public override void Paint(float width, float height) {
            Graphics2D.SetDrawColor(20, 25, 32, 127);
            Graphics2D.DrawRectangle(0, 0, width, height);
            Graphics2D.SetDrawColor(85, 95, 110);
            Graphics2D.DrawRectangleOutline(0, 0, width, height, 2);
        }

        public override T Add<T>(T? toAdd = null) where T : class {
            var item = base.Add<T>(toAdd);

            item.Dock = Dock.Top;
            //item.MouseReleaseEvent += Item_MouseReleaseEvent;
            return item;
        }
        public override bool ShouldItemBeVisible(Element e) {
            return (e as ListViewItem).ShowLVItem;
        }
    }
    public class ListViewItem : Button {
        private bool __isLVIVisible = true;

        public bool ShowLVItem {
            get => __isLVIVisible;
            set => __isLVIVisible = value;
        }

        protected override void Initialize() {
            base.Initialize();
            BackgroundColor = new(0, 0, 0, 0);
            ForegroundColor = new(0, 0, 0, 0);
            this.Clipping = false;
        }

        protected override void OnThink(FrameState frameState) {
            if (Depressed) {
                BackgroundColor = new(30, 35, 45, 65);
                EngineCore.SetMouseCursor(MouseCursor.MOUSE_CURSOR_POINTING_HAND);
            }
            else if (Hovered) {
                BackgroundColor = new(200, 210, 230, 50);
                EngineCore.SetMouseCursor(MouseCursor.MOUSE_CURSOR_POINTING_HAND);
            }
            else
                BackgroundColor = new(0, 0, 0, 0);
        }
    }
}
