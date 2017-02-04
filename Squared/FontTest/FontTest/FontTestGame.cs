using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.GamerServices;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Media;
using Squared.Game;
using Squared.Render;
using Squared.Render.Convenience;
using Squared.Render.Text;
using Squared.Util;

namespace FontTest {
    public class FontTestGame : MultithreadedGame {
        public static readonly Color ClearColor = new Color(24, 36, 40, 255);

        public string TestText =
            "The quick brown fox jumped over the lazy dogs.\r\n" +
            "Long woooooooooooooooooooooooord\r\n" +
            "a b c d e f g h i j k l m n o p q r s t u v w x y z";

        SpriteFont Font;

        DefaultMaterialSet Materials;
        GraphicsDeviceManager Graphics;

        DynamicStringLayout Text;

        public Vector2 Margin = new Vector2(24, 24);
        public Vector2? BottomRight;

        PressableKey Alignment = new PressableKey(Keys.A);
        PressableKey WordWrap = new PressableKey(Keys.W);

        public FontTestGame () {
            Graphics = new GraphicsDeviceManager(this);
            Graphics.PreferredBackBufferWidth = 1024;
            Graphics.PreferredBackBufferHeight = 1024;
            Content.RootDirectory = "Content";
            IsMouseVisible = true;
        }

        protected override void Initialize () {
            Materials = new DefaultMaterialSet(Services);

            base.Initialize();

            Alignment.Pressed += (s, e) => {
                Text.Alignment = (HorizontalAlignment)(((int)Text.Alignment + 1) % 3);
            };
            WordWrap.Pressed += (s, e) => {
                Text.WordWrap = !Text.WordWrap;
            };
        }

        protected override void LoadContent () {
            Font = Content.Load<SpriteFont>("font");

            Text = new DynamicStringLayout(Font, TestText) {
                // Alignment = HorizontalAlignment.Right,
                CharacterWrap = true,
                WordWrap = true
            };
        }

        protected override void UnloadContent () {
        }

        protected override void Update (GameTime gameTime) {
            base.Update(gameTime);

            if (!IsActive)
                return;

            var ms = Mouse.GetState();
            if (ms.LeftButton == ButtonState.Pressed)
                BottomRight = new Vector2(ms.X, ms.Y);
            else if (ms.RightButton == ButtonState.Pressed)
                BottomRight = null;

            var ks = Keyboard.GetState();
            Alignment.Update(ref ks);
            WordWrap.Update(ref ks);
        }

        public override void Draw (GameTime gameTime, Frame frame) {
            var ir = new ImperativeRenderer(frame, Materials, blendState: BlendState.AlphaBlend);
            ir.AutoIncrementLayer = true;

            ir.Clear(color: ClearColor);

            if (BottomRight.HasValue)
                Text.LineBreakAtX = BottomRight.Value.X - Margin.X;
            else
                Text.LineBreakAtX = Arithmetic.Pulse((float)(gameTime.TotalGameTime.TotalSeconds / 14), 0, 1024);

            ir.OutlineRectangle(new Bounds(Margin, new Vector2(Text.LineBreakAtX.Value + Margin.X, 1024 - Margin.Y)), Color.Red);

            var layout = Text.Get();

            foreach (var dc in layout.DrawCalls)
                ir.OutlineRectangle(dc.EstimateDrawBounds().Translate(Margin), Color.Blue);

            ir.OutlineRectangle(Bounds.FromPositionAndSize(Margin, layout.Size), Color.Yellow * 0.75f);

            ir.DrawMultiple(layout, Margin);
        }
    }

    public class PressableKey {
        public readonly Keys Key;
        public event EventHandler Pressed;

        private bool previousState;

        public PressableKey (Keys key, EventHandler pressed = null) {
            Key = key;
            Pressed = pressed;
        }

        public void Update (ref KeyboardState ks) {
            var state = ks.IsKeyDown(Key);
            if ((state != previousState) && (Pressed != null) && state)
                Pressed(this, EventArgs.Empty);
            previousState = state;
        }
    }
}