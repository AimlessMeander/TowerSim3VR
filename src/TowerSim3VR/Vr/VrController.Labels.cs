using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace TowerSim3VR
{
    // Aircraft labels in the world. On the monitor the game draws its labels (VTag: call sign, runway box, a line
    // down to the aircraft) on a 2D overlay placed with the game camera, so they can't be seen in the headset. Here
    // the same labels are built as billboards above each aircraft, facing the head, at a fixed angular size (the
    // Labels.Size setting). They use the game's own rules for which aircraft get one and when it fades out, the
    // colours of its label prefab, and its font; they are hidden behind the tower's structure as on the monitor.
    // Labels that would overlap are stacked. Drawn with the other overlays (VrController.Overlay.cs), first, so the
    // lasers and screens go over them.
    //
    // The text is generated at the pixel size it covers in the eye image: the font texture has no mipmaps, so text
    // generated larger and shrunk on screen breaks up.
    public partial class VrController
    {
        const float LabelPadX = 0.35f, LabelPadY = 0.2f; // in units: 1 unit = the text's height
        const float LabelHeight = 1f + 2f * LabelPadY;
        const float LabelGap = 1.6f;                      // from the aircraft up to the box's bottom
        const float LabelStackMargin = 0.15f;
        const float LabelLineWidth = 0.08f;
        const int TowerStructureLayers = 1 << 23;         // what hides a label on the monitor too (VTag)

        struct TextShape
        {
            public Vector3[] positions; // in units, centred on the origin
            public Vector2[] uvs;
            public float width;
        }

        class LabelEntry
        {
            public Airplane plane;
            public Vector3 anchor;
            public float distance, alpha;
            public string callsign, runway;
            public TextShape callsignText, runwayText;
            public float callsignWidth, runwayWidth;
            public Color background, textColour, runwayColour;
            public float x, y;   // the anchor's direction from the head, in units of the label's angular size
            public float lift;   // extra height when stacked above another label
            public float Width => callsignWidth + runwayWidth;
            public float Bottom => y + LabelGap + lift;
        }

        VTag labelStyle;          // the game's label prefab: colours and font
        float labelSearchTime = -10f;
        Font labelFont;
        FontStyle labelFontStyle;
        int labelFontPixels;
        bool labelFontRebuilt;
        bool labelFontHooked;
        readonly TextGenerator labelGenerator = new TextGenerator();
        readonly Dictionary<string, TextShape> labelTexts = new Dictionary<string, TextShape>();
        readonly List<LabelEntry> labelEntries = new List<LabelEntry>();
        readonly List<LabelEntry> labelPool = new List<LabelEntry>();
        readonly Dictionary<Airplane, string> labelCallsigns = new Dictionary<Airplane, string>();

        Mesh labelShapes, labelGlyphs;
        Material labelShapeMaterial, labelTextMaterial;
        bool labelsReady;
        readonly List<Vector3> shapeVerts = new List<Vector3>(), glyphVerts = new List<Vector3>();
        readonly List<Color32> shapeColors = new List<Color32>(), glyphColors = new List<Color32>();
        readonly List<Vector2> glyphUvs = new List<Vector2>();
        readonly List<int> shapeTris = new List<int>(), glyphTris = new List<int>();
        readonly Vector3[] labelCorners = new Vector3[4];

        // Once per frame, before drawing the eyes: rebuilds the label meshes for the head's current pose.
        void BuildLabels()
        {
            labelsReady = false;
            if (!Plugin.Labels.Value || head == null || leftEye == null || leftTex == null) return;
            var game = Game.instance;
            if (game == null || game.active_airplanes == null || game.cmdwin == null) return;
            if (!FindLabelStyle()) return;

            var headPosition = head.position;
            labelEntries.Clear();
            foreach (var plane in game.active_airplanes)
            {
                if (plane == null) continue;
                float alpha = LabelAlpha(plane, headPosition, out var anchor, out float distance);
                if (alpha <= 0f) continue;
                if (labelPool.Count <= labelEntries.Count) labelPool.Add(new LabelEntry());
                var entry = labelPool[labelEntries.Count];
                entry.plane = plane;
                entry.anchor = anchor;
                entry.distance = distance;
                entry.alpha = alpha;
                labelEntries.Add(entry);
            }
            if (labelEntries.Count == 0) return;

            // One unit (the text's height) covers this angle; the text is generated at the pixels that is in the
            // eye image, so the font texture is drawn about 1:1.
            float degrees = Mathf.Max(0.05f, Plugin.LabelSize.Value);
            float unitDegrees = degrees / zoom;
            float pixelsPerRadian = leftTex.height * 0.5f * leftEye.projectionMatrix.m11 / zoom;
            int pixels = Mathf.Clamp(Mathf.RoundToInt(degrees * Mathf.Deg2Rad * pixelsPerRadian), 10, 128);
            if (pixels != labelFontPixels)
            {
                labelFontPixels = pixels;
                labelTexts.Clear();
            }

            // The game's own texts share the font texture, and when it fills up the texture is rebuilt with every
            // glyph in a new place; the laid-out texts are kept until then. All characters are put into the texture
            // first (nothing to do when they are there), then the texts are laid out, again if that rebuilt it.
            foreach (var entry in labelEntries) SetLabelTexts(entry);
            for (int attempt = 0; attempt < 3; attempt++)
            {
                foreach (var entry in labelEntries)
                {
                    labelFont.RequestCharactersInTexture(entry.callsign, labelFontPixels, labelFontStyle);
                    if (entry.runway != null) labelFont.RequestCharactersInTexture(entry.runway, labelFontPixels, labelFontStyle);
                }
                if (labelFontRebuilt)
                {
                    labelFontRebuilt = false;
                    labelTexts.Clear();
                }
                foreach (var entry in labelEntries)
                {
                    entry.callsignText = GetTextShape(entry.callsign);
                    if (entry.runway != null) entry.runwayText = GetTextShape(entry.runway);
                }
                if (!labelFontRebuilt) break;
            }

            float headYaw = head.eulerAngles.y;
            foreach (var entry in labelEntries)
            {
                entry.callsignWidth = entry.callsignText.width + 2f * LabelPadX;
                entry.runwayWidth = entry.runway != null ? entry.runwayText.width + 2f * LabelPadX : 0f;
                var direction = (entry.anchor - headPosition) / Mathf.Max(0.01f, entry.distance);
                float pitch = Mathf.Asin(Mathf.Clamp(direction.y, -1f, 1f)) * Mathf.Rad2Deg;
                float yaw = Mathf.DeltaAngle(headYaw, Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg);
                entry.x = yaw * Mathf.Cos(pitch * Mathf.Deg2Rad) / unitDegrees;
                entry.y = pitch / unitDegrees;
                entry.lift = 0f;
            }

            // Stacking: nearest first; a label that would overlap one already placed goes up above it.
            labelEntries.Sort((a, b) => a.distance.CompareTo(b.distance));
            for (int i = 1; i < labelEntries.Count; i++)
            {
                var entry = labelEntries[i];
                for (int tries = 0; tries < 20; tries++)
                {
                    bool moved = false;
                    for (int j = 0; j < i; j++)
                    {
                        var other = labelEntries[j];
                        if (Mathf.Abs(entry.x - other.x) * 2f >= entry.Width + other.Width + LabelStackMargin) continue;
                        if (entry.Bottom >= other.Bottom + LabelHeight + LabelStackMargin) continue;
                        if (entry.Bottom + LabelHeight + LabelStackMargin <= other.Bottom) continue;
                        entry.lift = other.Bottom + LabelHeight + LabelStackMargin - entry.y - LabelGap;
                        moved = true;
                    }
                    if (!moved) break;
                }
            }

            shapeVerts.Clear(); shapeColors.Clear(); shapeTris.Clear();
            glyphVerts.Clear(); glyphColors.Clear(); glyphUvs.Clear(); glyphTris.Clear();
            for (int i = labelEntries.Count - 1; i >= 0; i--) // far first, near on top
            {
                var entry = labelEntries[i];
                // Faces the head, upright; sized so the text covers the same angle at any distance (and not
                // magnified by the binoculars).
                var rotation = Quaternion.LookRotation(entry.anchor - headPosition, Vector3.up);
                float scale = entry.distance * unitDegrees * Mathf.Deg2Rad;
                AddLabel(entry, rotation * Vector3.right * scale, rotation * Vector3.up * scale, headPosition);
            }
            if (shapeVerts.Count == 0) return;

            EnsureLabelMeshes();
            FillMesh(labelShapes, shapeVerts, shapeColors, null, shapeTris);
            FillMesh(labelGlyphs, glyphVerts, glyphColors, glyphUvs, glyphTris);
            labelTextMaterial.mainTexture = labelFont.material.mainTexture;
            labelsReady = true;
        }

        void DrawLabels(CommandBuffer cmd)
        {
            if (!labelsReady) return;
            cmd.DrawMesh(labelShapes, Matrix4x4.identity, labelShapeMaterial, 0, 0);
            cmd.DrawMesh(labelGlyphs, Matrix4x4.identity, labelTextMaterial, 0, 0);
        }

        // The game's rules (VTag.LateUpdate), without its checks against the game camera's view: the label shows
        // for departures and for arrivals that have called in, fades out with distance (much further for aircraft
        // in the air) and is dimmer for aircraft on a frequency you don't work.
        float LabelAlpha(Airplane plane, Vector3 headPosition, out Vector3 anchor, out float distance)
        {
            anchor = plane.transform.position + Vector3.up * 8f;
            distance = Vector3.Distance(headPosition, anchor);
            if (!plane._active || plane._owner == null || !(plane.isDeparture() || plane._trycontact)) return 0f;

            bool landingOrTakeoff = plane._trgrunway != null && (plane._takeoff || plane._landtrick != Airplane.LANDING_TRICKS.LT_FLYOVER);
            float reach;
            if (landingOrTakeoff && plane.isApproach()) reach = plane._alt >= 100f ? 10f : plane._alt > 10f ? plane._alt / 10f : 1f;
            else reach = plane._alt >= 100f ? 20f : plane._alt > 5f ? plane._alt / 5f : 1f;
            float scaled = Vector3.Distance(headPosition, plane.transform.position) / reach / 2f;
            float alpha = Mathf.Clamp01((4000f - scaled) / 500f);
            if (alpha <= 0f) return 0f;
            if (!OnOwnFrequency(plane)) alpha *= 0.8f;
            return alpha;
        }

        static bool OnOwnFrequency(Airplane plane)
        {
            var me = TCP.Me;
            return me == null || me.own == null || me.own.Contains(plane._owner.freq);
        }

        // The label's texts and colours, as VTag sets them.
        void SetLabelTexts(LabelEntry entry)
        {
            var plane = entry.plane;
            var style = labelStyle;
            bool own = OnOwnFrequency(plane);

            // Made once per aircraft (reading the name makes a new string each time).
            if (!labelCallsigns.TryGetValue(plane, out entry.callsign))
            {
                if (labelCallsigns.Count > 300) labelCallsigns.Clear();
                entry.callsign = plane.name;
                if (plane._type != null)
                {
                    if (plane._type._weight_class == Airptype.WEIGHT_CLASS.H) entry.callsign += "/H";
                    else if (plane._type._weight_class == Airptype.WEIGHT_CLASS.J) entry.callsign += "/J";
                }
                labelCallsigns[plane] = entry.callsign;
            }
            entry.runway = own && plane._trgrunway != null ? plane._trgrunway.name ?? "" : null;
            bool cleared = plane._trgrunway != null && (plane._takeoff || plane._landtrick != Airplane.LANDING_TRICKS.LT_FLYOVER);
            bool lineUp = plane._trgrunway != null && (plane._lineup_and_wait || plane._waitnext_and_lineup >= 0) && plane._alt <= 0f;

            entry.background = !own ? style.color_bg_disabled : plane.isApproach() ? style.color_arriving : style.color_departure;
            entry.textColour = own && Game.instance.cmdwin.prev_cap == plane ? style.color_selected : style.color_unselected;
            entry.runwayColour = cleared ? style.color_runway_allow : lineUp ? style.color_runway_lineup : style.color_runway;
        }

        void AddLabel(LabelEntry entry, Vector3 right, Vector3 up, Vector3 headPosition)
        {
            float width = entry.Width;
            float left = -width / 2f, bottom = LabelGap + entry.lift, top = bottom + LabelHeight;

            // Hidden behind the tower's frame and desk, as on the monitor: any corner of the box behind them.
            var origin = entry.anchor;
            labelCorners[0] = origin + right * left + up * bottom;
            labelCorners[1] = origin + right * -left + up * bottom;
            labelCorners[2] = origin + right * left + up * top;
            labelCorners[3] = origin + right * -left + up * top;
            foreach (var corner in labelCorners)
            {
                var ray = corner - headPosition;
                if (Physics.Raycast(headPosition, ray, out var hit, Mathf.Min(200f, ray.magnitude), TowerStructureLayers)
                    && !hit.collider.CompareTag("rolo"))
                {
                    return;
                }
            }

            float alpha = entry.alpha;
            float middle = bottom + LabelHeight / 2f;
            AddQuad(origin, right, up, -LabelLineWidth / 2f, 0.3f, LabelLineWidth / 2f, bottom, WithAlpha(entry.background, alpha));
            AddQuad(origin, right, up, left, bottom, left + entry.callsignWidth, top, WithAlpha(entry.background, alpha));
            AddText(entry.callsignText, origin + right * (left + entry.callsignWidth / 2f) + up * middle, right, up,
                WithAlpha(entry.textColour, alpha));
            if (entry.runway != null)
            {
                AddQuad(origin, right, up, left + entry.callsignWidth, bottom, -left, top, WithAlpha(entry.runwayColour, alpha));
                AddText(entry.runwayText, origin + right * (left + entry.callsignWidth + entry.runwayWidth / 2f) + up * middle, right, up,
                    WithAlpha(Color.white, alpha));
            }
        }

        static Color32 WithAlpha(Color colour, float alpha)
        {
            colour.a *= alpha;
            return colour;
        }

        void AddQuad(Vector3 origin, Vector3 right, Vector3 up, float x0, float y0, float x1, float y1, Color32 colour)
        {
            int start = shapeVerts.Count;
            shapeVerts.Add(origin + right * x0 + up * y0);
            shapeVerts.Add(origin + right * x0 + up * y1);
            shapeVerts.Add(origin + right * x1 + up * y1);
            shapeVerts.Add(origin + right * x1 + up * y0);
            for (int i = 0; i < 4; i++) shapeColors.Add(colour);
            shapeTris.Add(start); shapeTris.Add(start + 1); shapeTris.Add(start + 2);
            shapeTris.Add(start); shapeTris.Add(start + 2); shapeTris.Add(start + 3);
        }

        void AddText(TextShape text, Vector3 centre, Vector3 right, Vector3 up, Color32 colour)
        {
            if (text.positions == null) return;
            int start = glyphVerts.Count;
            for (int i = 0; i < text.positions.Length; i++)
            {
                var p = text.positions[i];
                glyphVerts.Add(centre + right * p.x + up * p.y);
                glyphUvs.Add(text.uvs[i]);
                glyphColors.Add(colour);
            }
            // Four vertices per character, clockwise from the top left (as Unity's Text builds them).
            for (int i = 0; i + 3 < text.positions.Length; i += 4)
            {
                glyphTris.Add(start + i); glyphTris.Add(start + i + 1); glyphTris.Add(start + i + 2);
                glyphTris.Add(start + i + 2); glyphTris.Add(start + i + 3); glyphTris.Add(start + i);
            }
        }

        TextShape GetTextShape(string text)
        {
            if (labelTexts.TryGetValue(text, out var shape)) return shape;
            if (labelTexts.Count > 500) labelTexts.Clear();

            var settings = new TextGenerationSettings
            {
                font = labelFont,
                fontSize = labelFontPixels,
                fontStyle = labelFontStyle,
                color = Color.white,
                pivot = new Vector2(0.5f, 0.5f),
                textAnchor = TextAnchor.MiddleCenter,
                generationExtents = new Vector2(8000f, 800f),
                horizontalOverflow = HorizontalWrapMode.Overflow,
                verticalOverflow = VerticalWrapMode.Overflow,
                lineSpacing = 1f,
                richText = false,
                scaleFactor = 1f,
                generateOutOfBounds = true,
            };
            // Populate returns its previous result unchanged for the same text and settings, even after the font
            // texture was rebuilt (Unity's Text invalidates it then); so it is always made to generate afresh.
            labelGenerator.Invalidate();
            labelGenerator.Populate(text, settings);
            var verts = labelGenerator.verts;
            shape.positions = new Vector3[verts.Count];
            shape.uvs = new Vector2[verts.Count];
            float minX = 0f, maxX = 0f;
            for (int i = 0; i < verts.Count; i++)
            {
                var p = verts[i].position / labelFontPixels;
                shape.positions[i] = p;
                shape.uvs[i] = verts[i].uv0;
                if (i == 0 || p.x < minX) minX = p.x;
                if (i == 0 || p.x > maxX) maxX = p.x;
            }
            shape.width = maxX - minX;
            labelTexts[text] = shape;
            return shape;
        }

        bool FindLabelStyle()
        {
            if (labelStyle == null || labelFont == null)
            {
                labelStyle = null;
                if (Time.unscaledTime - labelSearchTime < 2f) return false;
                labelSearchTime = Time.unscaledTime;
                var manager = FindObjectOfType<VirtualControlManager>();
                var prefab = manager != null && manager.prefab_vtag != null ? manager.prefab_vtag.GetComponent<VTag>() : null;
                if (prefab == null) return false;
                labelStyle = prefab;
                bool gameFont = prefab.txt_tag != null && prefab.txt_tag.font != null && prefab.txt_tag.font.dynamic;
                labelFont = gameFont ? prefab.txt_tag.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                labelFontStyle = gameFont ? prefab.txt_tag.fontStyle : FontStyle.Normal;
                labelTexts.Clear();
                Log.LogInfo($"Aircraft labels use the font '{labelFont.name}'");
                if (!labelFontHooked)
                {
                    labelFontHooked = true;
                    Font.textureRebuilt += font => { if (font == labelFont) labelFontRebuilt = true; };
                }
            }
            return true;
        }

        void EnsureLabelMeshes()
        {
            if (labelShapes != null) return;
            labelShapes = new Mesh { name = "TowerSim3VR_LabelShapes", indexFormat = IndexFormat.UInt32 };
            labelGlyphs = new Mesh { name = "TowerSim3VR_LabelText", indexFormat = IndexFormat.UInt32 };
            labelShapes.MarkDynamic();
            labelGlyphs.MarkDynamic();
            labelShapeMaterial = MakeUnlitColor(Color.white);
            labelShapeMaterial.mainTexture = Texture2D.whiteTexture;
            labelShapeMaterial.SetVector("_TextureSampleAdd", Vector4.zero);
            // The font texture holds only alpha; as for Unity's UI text, white is added to its colour.
            labelTextMaterial = MakeUnlitColor(Color.white);
            labelTextMaterial.SetVector("_TextureSampleAdd", new Vector4(1f, 1f, 1f, 0f));
        }

        static void FillMesh(Mesh mesh, List<Vector3> verts, List<Color32> colours, List<Vector2> uvs, List<int> tris)
        {
            mesh.Clear();
            mesh.SetVertices(verts);
            mesh.SetColors(colours);
            if (uvs != null) mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0, false);
        }
    }
}
