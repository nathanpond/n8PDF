using System.Text;

namespace n8PDF.Images;

/// <summary>
/// Reads the records of a metafile's second format.
/// </summary>
/// <remarks>
/// A metafile written by anything modern carries two drawings, not one. The old records draw with
/// the interface Windows has always had; the new ones draw with the one that replaced it, and they
/// travel inside the comments of the old — a format smuggled through a format. A comment that
/// opens with the letters "EMF+" is not a comment at all.
///
/// The two are alternatives rather than halves: a file carrying both draws the same picture either
/// way, so whichever is read, the other is passed over. The new records are the ones read here
/// where a file has them, since they are what a chart pasted out of a spreadsheet is made of and
/// the old ones are often only a rough copy of it.
///
/// What is different about them, beyond the numbering, is that they are properly transformed. A
/// point goes through the world transform the drawing has set, then through the page transform
/// that says what its units mean, and only then is it anywhere. Both are matrices, and they are
/// kept as matrices here rather than reduced to a scale.
/// </remarks>
internal static class EmfPlusInterpreter
{
    /// <summary>The letters a comment opens with when it is not a comment.</summary>
    public static bool IsEmfPlusComment(byte[] data, int at, int length) =>
        length >= 4 && at + 3 < data.Length &&
        data[at] == 0x45 && data[at + 1] == 0x4D && data[at + 2] == 0x46 && data[at + 3] == 0x2B;

    /// <summary>
    /// Reads a run of records, which is every EMF+ comment of a file joined together: a record may
    /// begin in one comment and end in the next.
    /// </summary>
    public static List<DrawingOperation> Read(byte[] records, double unitsToPoints)
    {
        var state = new Interpreter(unitsToPoints, [], ImageLimits.DefaultMaximumPixels, 0);
        state.Feed(records, 0, records.Length);

        return state.Operations;
    }

    /// <summary>
    /// Begins reading, a comment at a time, so that the records of both formats can be replayed in
    /// the one order the file puts them in.
    /// </summary>
    internal static Reader Begin(
        double unitsToPoints, List<DrawingOperation> operations, long maximumPixels, int nesting) =>
        new(unitsToPoints, operations, maximumPixels, nesting);

    /// <summary>Reads the records as they arrive, holding on to what a comment cut in half.</summary>
    internal sealed class Reader(
        double unitsToPoints, List<DrawingOperation> operations, long maximumPixels, int nesting)
    {
        private readonly Interpreter _interpreter = new(unitsToPoints, operations, maximumPixels, nesting);

        /// <summary>
        /// Whether the last record read handed the drawing back to the older interface, after
        /// which its records are the ones that draw until these resume.
        /// </summary>
        public bool HandedBack => _interpreter.HandedBack;

        public void Feed(byte[] data, int from, int to) => _interpreter.Feed(data, from, to);
    }

    /// <summary>An affine transform, which is what both of a drawing's transforms are.</summary>
    private readonly record struct Matrix(double M11, double M12, double M21, double M22, double Dx, double Dy)
    {
        public static readonly Matrix Identity = new(1, 0, 0, 1, 0, 0);

        public (double X, double Y) Apply(double x, double y) =>
            (M11 * x + M21 * y + Dx, M12 * x + M22 * y + Dy);

        /// <summary>This transform followed by another.</summary>
        public Matrix Then(Matrix other) => new(
            M11 * other.M11 + M12 * other.M21,
            M11 * other.M12 + M12 * other.M22,
            M21 * other.M11 + M22 * other.M21,
            M21 * other.M12 + M22 * other.M22,
            Dx * other.M11 + Dy * other.M21 + other.Dx,
            Dx * other.M12 + Dy * other.M22 + other.Dy);

        /// <summary>How much this transform scales, which is what a pen's width follows.</summary>
        public double Scale => Math.Sqrt(Math.Abs(M11 * M22 - M12 * M21));
    }

    private sealed class Interpreter(
        double unitsToPoints, List<DrawingOperation> operations, long maximumPixels, int nesting)
    {
        public List<DrawingOperation> Operations => operations;

        /// <summary>Whatever is waiting to be read: a record a comment ended in the middle of.</summary>
        private byte[] data = [];

        public bool HandedBack { get; private set; }

        private readonly Dictionary<int, object> _objects = [];
        private readonly Stack<State> _saved = new();

        private State _state = new();
        private double _dpiX = 96;
        private double _dpiY = 96;

        private sealed record State
        {
            public Matrix World { get; init; } = Matrix.Identity;

            /// <summary>What the drawing's own units mean, which the page transform says.</summary>
            public double PageScale { get; init; } = 1;
        }

        private sealed record Pen(DrawingColor Color, double Width);

        private sealed record PlusFont(string Family, double Size, bool Bold, bool Italic);

        private sealed record PlusPath(List<PathStep> Steps);

        /// <summary>
        /// Reads what has arrived. A record may begin in one comment and end in the next, so
        /// whatever is left over at the end of a run waits for the run that completes it.
        /// </summary>
        /// <summary>
        /// The most that may wait for a record to complete across comments. A single EMF+ record
        /// is small; a pending buffer past this is a record that declares a size it never fills,
        /// re-copied whole on every comment that follows (#19), so it is abandoned rather than
        /// carried.
        /// </summary>
        private const int MaxPending = 16 * 1024 * 1024;

        public void Feed(byte[] arrived, int from, int to)
        {
            if (to <= from) return;

            // Abandon a leftover that has grown past what any real record needs, rather than
            // append to it and re-copy it again (#19).
            if ((long)data.Length + (to - from) > MaxPending)
            {
                data = [];
                return;
            }

            data = data.Length == 0 ? arrived[from..to] : [.. data, .. arrived[from..to]];

            var at = 0;

            while (at + 12 <= data.Length)
            {
                var type = ReadUInt16(at);
                var flags = ReadUInt16(at + 2);
                var size = (int)ReadUInt32(at + 4);

                if (size < 12)
                {
                    // Nothing further can be made sense of, so nothing further is read.
                    data = [];

                    return;
                }

                if (size > data.Length - at) break;  // overflow-safe (#18)

                Record(type, flags, at + 12, at + size);

                // Handing back lasts until the next of these records, whatever it is.
                HandedBack = type == 0x4004;

                at += size;
            }

            data = data[at..];
        }

        private void Record(int type, int flags, int at, int end)
        {
            switch (type)
            {
                case 0x4001: // Header
                    _dpiX = ReadFloat(at + 8) > 0 ? ReadFloat(at + 8) : 96;
                    _dpiY = ReadFloat(at + 12) > 0 ? ReadFloat(at + 12) : 96;
                    break;

                case 0x4008: // Object
                    Object(flags, at, end);
                    break;

                case 0x4025: // Save
                case 0x4027: // BeginContainer
                case 0x4028: // BeginContainerNoParams
                    _saved.Push(_state);
                    break;

                case 0x4026: // Restore
                case 0x4029: // EndContainer
                    if (_saved.Count > 0) _state = _saved.Pop();
                    break;

                case 0x402A: // SetWorldTransform
                    _state = _state with { World = ReadMatrix(at) };
                    break;

                case 0x402B: // ResetWorldTransform
                    _state = _state with { World = Matrix.Identity };
                    break;

                case 0x402C: // MultiplyWorldTransform
                {
                    var matrix = ReadMatrix(at);

                    // The flag says which side it goes on: before what is there, or after it.
                    _state = _state with
                    {
                        World = (flags & 0x2000) != 0 ? _state.World.Then(matrix) : matrix.Then(_state.World)
                    };

                    break;
                }

                case 0x402D: // TranslateWorldTransform
                {
                    var move = new Matrix(1, 0, 0, 1, ReadFloat(at), ReadFloat(at + 4));

                    _state = _state with
                    {
                        World = (flags & 0x2000) != 0 ? _state.World.Then(move) : move.Then(_state.World)
                    };

                    break;
                }

                case 0x402E: // ScaleWorldTransform
                {
                    var scale = new Matrix(ReadFloat(at), 0, 0, ReadFloat(at + 4), 0, 0);

                    _state = _state with
                    {
                        World = (flags & 0x2000) != 0 ? _state.World.Then(scale) : scale.Then(_state.World)
                    };

                    break;
                }

                case 0x402F: // RotateWorldTransform
                {
                    var radians = ReadFloat(at) * Math.PI / 180;
                    var cos = Math.Cos(radians);
                    var sin = Math.Sin(radians);
                    var turn = new Matrix(cos, sin, -sin, cos, 0, 0);

                    _state = _state with
                    {
                        World = (flags & 0x2000) != 0 ? _state.World.Then(turn) : turn.Then(_state.World)
                    };

                    break;
                }

                case 0x4030: // SetPageTransform
                    _state = _state with { PageScale = PageScale(flags & 0xff, ReadFloat(at)) };
                    break;

                // ----- what is drawn -----

                case 0x400B: // FillRects
                    FillRects(flags, at, end);
                    break;

                case 0x400C: // DrawRects
                    DrawRects(flags, at, end);
                    break;

                case 0x400D: // FillPolygon
                    FillPolygon(flags, at, end);
                    break;

                case 0x400E: // DrawLines
                    DrawLines(flags, at, end);
                    break;

                case 0x400F: // FillEllipse
                    Ellipse(flags, at + 4, Brush(flags, ReadUInt32(at)), null);
                    break;

                case 0x4010: // DrawEllipse
                    Ellipse(flags, at, null, PenOf(flags));
                    break;

                case 0x4015: // FillPath
                    Path(flags, Brush(flags, ReadUInt32(at)), null);
                    break;

                case 0x4016: // DrawPath
                    // The path is named in the flags and the pen in the record, which is the
                    // other way round from every other record that draws with one.
                    Path(flags, null, PenOf((int)ReadUInt32(at)));
                    break;

                case 0x401A: // DrawBeziers
                    Beziers(flags, at, end);
                    break;

                case 0x401B: // DrawImage
                    Image(flags, at, end);
                    break;

                case 0x401D: // DrawString
                    String(flags, at, end);
                    break;
            }
        }

        /// <summary>
        /// What a drawing's units mean, which is the page transform: a scale, and the unit it is
        /// a scale of.
        /// </summary>
        private double PageScale(int unit, double scale) => unit switch
        {
            3 => scale * _dpiX / 72,     // points
            4 => scale * _dpiX,          // inches
            5 => scale * _dpiX / 300,    // document units, which are three hundredths of an inch
            6 => scale * _dpiX / 25.4,   // millimetres
            _ => scale                   // world, display and pixels are the device's own
        };

        // ----- the objects -----

        private void Object(int flags, int at, int end)
        {
            var id = flags & 0xff;
            var kind = (flags >> 8) & 0x7f;

            switch (kind)
            {
                case 0: // brush
                    if (ReadUInt32(at + 4) == 0) _objects[id] = Color(ReadUInt32(at + 8));
                    break;

                case 1: // pen
                    _objects[id] = ReadPen(at, end);
                    break;

                case 2: // path
                    _objects[id] = new PlusPath(ReadPath(at, end));
                    break;

                case 5: // font
                    _objects[id] = ReadFont(at);
                    break;

                case 4: // image
                    if (ReadImage(at, end) is { } image) _objects[id] = image;
                    break;
            }
        }

        /// <summary>
        /// A pen, which is a width and the brush it paints with. What lies between them is a run
        /// of optional fields, each present only where a flag says so, so the brush can only be
        /// found by stepping over whichever of them are there.
        /// </summary>
        private Pen ReadPen(int at, int end)
        {
            var flags = (int)ReadUInt32(at + 8);
            var width = ReadFloat(at + 16);

            var next = at + 20;

            if ((flags & 0x0001) != 0) next += 24;   // a transform of its own
            if ((flags & 0x0002) != 0) next += 4;    // the cap it starts with
            if ((flags & 0x0004) != 0) next += 4;    // the cap it ends with
            if ((flags & 0x0008) != 0) next += 4;    // how its corners join
            if ((flags & 0x0010) != 0) next += 4;    // how far a corner may reach
            if ((flags & 0x0020) != 0) next += 4;    // whether it is dashed
            if ((flags & 0x0040) != 0) next += 4;    // the cap of a dash
            if ((flags & 0x0080) != 0) next += 4;    // where the dashes start

            if ((flags & 0x0100) != 0)
            {
                next += 4 + (int)ReadUInt32(next) * 4;
            }

            if ((flags & 0x0200) != 0) next += 4;    // where the line sits against the path

            if ((flags & 0x0400) != 0)
            {
                next += 4 + (int)ReadUInt32(next) * 4;
            }

            if ((flags & 0x0800) != 0) next += 4 + (int)ReadUInt32(next);
            if ((flags & 0x1000) != 0) next += 4 + (int)ReadUInt32(next);

            // What is left is a brush, and only a plain one has a colour to take.
            var colour = next + 12 <= end && ReadUInt32(next + 4) == 0
                ? Color(ReadUInt32(next + 8))
                : new DrawingColor(0, 0, 0);

            return new Pen(colour, Math.Max(0, width));
        }

        private PlusFont ReadFont(int at)
        {
            var size = ReadFloat(at + 4);
            var unit = (int)ReadUInt32(at + 8);
            var style = (int)ReadUInt32(at + 12);
            var length = (int)ReadUInt32(at + 20);

            var name = new StringBuilder();
            for (var i = 0; i < length && at + 24 + i * 2 + 1 < data.Length; i++)
                name.Append((char)ReadUInt16(at + 24 + i * 2));

            // The size is in whatever unit the font names, and what comes out is points.
            var points = unit switch
            {
                3 => size,                  // already points
                4 => size * 72,             // inches
                5 => size * 72 / 300.0,     // document units
                6 => size * 72 / 25.4,      // millimetres
                _ => size * 72 / _dpiY      // pixels of the device
            };

            return new PlusFont(
                name.Length > 0 ? name.ToString() : "Helvetica",
                points, (style & 1) != 0, (style & 2) != 0);
        }

        private ImageData? ReadImage(int at, int end)
        {
            // Only a bitmap, and only one kept as a file of its own — which is how a picture put
            // into a drawing by anything modern is kept.
            if (ReadUInt32(at + 4) != 1) return null;
            if (ReadUInt32(at + 20) != 1) return null;

            var start = at + 24;
            if (start >= end) return null;

            // A picture inside a drawing, read as the file it is — and counted, because that
            // file may be another drawing holding another picture. See ImageLimits.MaximumNesting.
            return ImageReader.TryRead(data[start..end], maximumPixels, nesting + 1);
        }

        // ----- what is drawn -----

        private DrawingColor? Brush(int flags, uint id) =>
            (flags & 0x8000) != 0
                ? Color(id)
                : _objects.TryGetValue((int)id & 0xff, out var found) && found is DrawingColor colour
                    ? colour
                    : new DrawingColor(0, 0, 0);

        private Pen? PenOf(int id) =>
            _objects.TryGetValue(id & 0xff, out var found) && found is Pen pen ? pen : null;

        private void FillRects(int flags, int at, int end)
        {
            var fill = Brush(flags, ReadUInt32(at));
            var count = (int)ReadUInt32(at + 4);
            var compressed = (flags & 0x4000) != 0;

            var next = at + 8;

            for (var i = 0; i < count; i++)
            {
                if (ReadRect(ref next, compressed, end) is not { } rect) break;

                Emit(Box(rect), fill, null, 0);
            }
        }

        private void DrawRects(int flags, int at, int end)
        {
            var pen = PenOf(flags);
            var count = (int)ReadUInt32(at);
            var compressed = (flags & 0x4000) != 0;

            var next = at + 4;

            for (var i = 0; i < count; i++)
            {
                if (ReadRect(ref next, compressed, end) is not { } rect) break;

                Emit(Box(rect), null, pen?.Color, pen?.Width ?? 1);
            }
        }

        private void FillPolygon(int flags, int at, int end)
        {
            var fill = Brush(flags, ReadUInt32(at));
            var count = (int)ReadUInt32(at + 4);
            var points = ReadPoints(at + 8, count, flags, end);

            if (points.Count == 0) return;

            var steps = new List<PathStep> { new(PathStepKind.Move, [points[0]]) };
            for (var i = 1; i < points.Count; i++) steps.Add(new PathStep(PathStepKind.Line, [points[i]]));
            steps.Add(new PathStep(PathStepKind.Close, []));

            Emit(steps, fill, null, 0);
        }

        private void DrawLines(int flags, int at, int end)
        {
            var pen = PenOf(flags);
            var count = (int)ReadUInt32(at);
            var points = ReadPoints(at + 4, count, flags, end);

            if (points.Count < 2) return;

            var steps = new List<PathStep> { new(PathStepKind.Move, [points[0]]) };
            for (var i = 1; i < points.Count; i++) steps.Add(new PathStep(PathStepKind.Line, [points[i]]));

            // The flag says whether the last point joins back to the first.
            if ((flags & 0x2000) != 0) steps.Add(new PathStep(PathStepKind.Close, []));

            Emit(steps, null, pen?.Color, pen?.Width ?? 1);
        }

        private void Beziers(int flags, int at, int end)
        {
            var pen = PenOf(flags);
            var count = (int)ReadUInt32(at);
            var points = ReadPoints(at + 4, count, flags, end);

            if (points.Count < 4) return;

            var steps = new List<PathStep> { new(PathStepKind.Move, [points[0]]) };

            for (var i = 1; i + 2 < points.Count; i += 3)
                steps.Add(new PathStep(PathStepKind.Curve, [points[i], points[i + 1], points[i + 2]]));

            Emit(steps, null, pen?.Color, pen?.Width ?? 1);
        }

        private void Ellipse(int flags, int at, DrawingColor? fill, Pen? pen)
        {
            var next = at;
            if (ReadRect(ref next, (flags & 0x4000) != 0, data.Length) is not { } rect) return;

            var (x, y, width, height) = rect;

            const double k = 0.5522847498;

            var cx = x + width / 2;
            var cy = y + height / 2;
            var rx = width / 2;
            var ry = height / 2;

            Emit(
                [
                    new PathStep(PathStepKind.Move, [Point(cx + rx, cy)]),
                    new PathStep(PathStepKind.Curve,
                        [Point(cx + rx, cy + ry * k), Point(cx + rx * k, cy + ry), Point(cx, cy + ry)]),
                    new PathStep(PathStepKind.Curve,
                        [Point(cx - rx * k, cy + ry), Point(cx - rx, cy + ry * k), Point(cx - rx, cy)]),
                    new PathStep(PathStepKind.Curve,
                        [Point(cx - rx, cy - ry * k), Point(cx - rx * k, cy - ry), Point(cx, cy - ry)]),
                    new PathStep(PathStepKind.Curve,
                        [Point(cx + rx * k, cy - ry), Point(cx + rx, cy - ry * k), Point(cx + rx, cy)]),
                    new PathStep(PathStepKind.Close, [])
                ],
                fill, pen?.Color, pen?.Width ?? 0, transformed: true);
        }

        private void Path(int flags, DrawingColor? fill, Pen? pen)
        {
            if (!_objects.TryGetValue(flags & 0xff, out var found) || found is not PlusPath path) return;

            Emit(path.Steps, fill, pen?.Color, pen?.Width ?? 0, transformed: true);
        }

        private void Image(int flags, int at, int end)
        {
            if (!_objects.TryGetValue(flags & 0xff, out var found) || found is not ImageData image) return;

            // The source rectangle says which part of the picture is wanted, which is all of it in
            // everything worth reading; the one after it says where it goes.
            var next = at + 24;
            if (ReadRect(ref next, (flags & 0x4000) != 0, end) is not { } rect) return;

            var (x, y) = Point(rect.X, rect.Y);
            var (right, bottom) = Point(rect.X + rect.Width, rect.Y + rect.Height);

            Operations.Add(new ImageOperation(image, x, y, right - x, bottom - y));
        }

        private void String(int flags, int at, int end)
        {
            var colour = Brush(flags & ~0x00ff | 0, ReadUInt32(at)) ?? new DrawingColor(0, 0, 0);

            // Where the brush is a colour outright the flag says so, as everywhere else.
            if ((flags & 0x8000) != 0) colour = Color(ReadUInt32(at));
            else if (_objects.TryGetValue((int)ReadUInt32(at) & 0xff, out var brush) && brush is DrawingColor found)
                colour = found;

            var length = (int)ReadUInt32(at + 8);
            var x = ReadFloat(at + 12);
            var y = ReadFloat(at + 16);

            var text = new StringBuilder();
            for (var i = 0; i < length && at + 28 + i * 2 + 1 < end; i++)
                text.Append((char)ReadUInt16(at + 28 + i * 2));

            if (text.Length == 0) return;

            var font = _objects.TryGetValue(flags & 0xff, out var value) && value is PlusFont named
                ? named
                : new PlusFont("Helvetica", 12, false, false);

            var (px, py) = Point(x, y);

            Operations.Add(new TextOperation(
                text.ToString(), px, py, font.Family,
                font.Size * _state.PageScale * unitsToPoints * _state.World.Scale,
                font.Bold, font.Italic, colour));
        }

        // ----- reading the numbers -----

        private List<(double X, double Y)> ReadPoints(int at, int count, int flags, int end)
        {
            // A point is at least two bytes (the relative form), so the record's own remaining
            // bytes bound how many there can be; the list is pre-sized from that rather than from
            // the raw count field (#22).
            var room = Math.Max(0, (end - at) / 2);
            count = Math.Min(Math.Max(0, count), room);

            var points = new List<(double, double)>(count);
            var compressed = (flags & 0x4000) != 0;
            var relative = (flags & 0x0800) != 0;

            double x = 0, y = 0;

            for (var i = 0; i < count; i++)
            {
                if (relative)
                {
                    // Each point is written as how far it is from the one before, in as few bytes
                    // as it will fit into.
                    if (at + 1 >= end) break;

                    x += (sbyte)data[at];
                    y += (sbyte)data[at + 1];
                    at += 2;
                }
                else if (compressed)
                {
                    if (at + 3 >= end) break;

                    x = ReadInt16(at);
                    y = ReadInt16(at + 2);
                    at += 4;
                }
                else
                {
                    if (at + 7 >= end) break;

                    x = ReadFloat(at);
                    y = ReadFloat(at + 4);
                    at += 8;
                }

                points.Add(Point(x, y));
            }

            return points;
        }

        private (double X, double Y, double Width, double Height)? ReadRect(ref int at, bool compressed, int end)
        {
            if (compressed)
            {
                if (at + 7 >= end) return null;

                var rect = ((double)ReadInt16(at), (double)ReadInt16(at + 2),
                    (double)ReadInt16(at + 4), (double)ReadInt16(at + 6));

                at += 8;
                return rect;
            }

            if (at + 15 >= end) return null;

            var full = ((double)ReadFloat(at), (double)ReadFloat(at + 4),
                (double)ReadFloat(at + 8), (double)ReadFloat(at + 12));

            at += 16;
            return full;
        }

        /// <summary>The path an object holds: its points, and what each of them does.</summary>
        private List<PathStep> ReadPath(int at, int end)
        {
            var count = (int)ReadUInt32(at + 4);
            var flags = (int)ReadUInt32(at + 8);

            if (count <= 0) return [];

            var points = ReadPoints(at + 12, count, flags, end);
            if (points.Count < count) return [];

            var typesAt = at + 12 + count * ((flags & 0x4000) != 0 ? 4 : 8);
            var steps = new List<PathStep>();

            for (var i = 0; i < count && typesAt + i < end; i++)
            {
                var kind = data[typesAt + i] & 0x07;
                var closes = (data[typesAt + i] & 0x80) != 0;

                switch (kind)
                {
                    case 0:
                        steps.Add(new PathStep(PathStepKind.Move, [points[i]]));
                        break;

                    case 3 when i + 2 < count:
                        steps.Add(new PathStep(PathStepKind.Curve, [points[i], points[i + 1], points[i + 2]]));

                        // A curve is three points and one step, so the two it took go with it.
                        closes = (data[Math.Min(typesAt + i + 2, end - 1)] & 0x80) != 0;
                        i += 2;
                        break;

                    default:
                        steps.Add(new PathStep(PathStepKind.Line, [points[i]]));
                        break;
                }

                if (closes) steps.Add(new PathStep(PathStepKind.Close, []));
            }

            return steps;
        }

        private List<PathStep> Box((double X, double Y, double Width, double Height) rect) =>
        [
            new(PathStepKind.Move, [Point(rect.X, rect.Y)]),
            new(PathStepKind.Line, [Point(rect.X + rect.Width, rect.Y)]),
            new(PathStepKind.Line, [Point(rect.X + rect.Width, rect.Y + rect.Height)]),
            new(PathStepKind.Line, [Point(rect.X, rect.Y + rect.Height)]),
            new(PathStepKind.Close, [])
        ];

        /// <summary>
        /// A point of the drawing's own, in the points of the page: through the world transform,
        /// then the page transform, then the metafile's own units.
        /// </summary>
        private (double X, double Y) Point(double x, double y)
        {
            var (wx, wy) = _state.World.Apply(x, y);

            return (wx * _state.PageScale * unitsToPoints, wy * _state.PageScale * unitsToPoints);
        }

        private void Emit(
            List<PathStep> steps, DrawingColor? fill, DrawingColor? stroke, double width,
            bool transformed = false)
        {
            if (steps.Count == 0 || (fill is null && stroke is null)) return;

            var placed = transformed ? steps : steps;

            Operations.Add(new PathOperation(
                placed, fill, stroke,
                Math.Max(0.24, width * _state.PageScale * unitsToPoints * _state.World.Scale), false));
        }

        private DrawingColor Color(uint value) =>
            new((byte)((value >> 16) & 0xff), (byte)((value >> 8) & 0xff), (byte)(value & 0xff));

        private Matrix ReadMatrix(int at) => new(
            ReadFloat(at), ReadFloat(at + 4), ReadFloat(at + 8),
            ReadFloat(at + 12), ReadFloat(at + 16), ReadFloat(at + 20));

        private uint ReadUInt32(int at) =>
            at + 3 < data.Length
                ? (uint)(data[at] | (data[at + 1] << 8) | (data[at + 2] << 16) | (data[at + 3] << 24))
                : 0;

        private int ReadUInt16(int at) => at + 1 < data.Length ? data[at] | (data[at + 1] << 8) : 0;

        private short ReadInt16(int at) => (short)ReadUInt16(at);

        private float ReadFloat(int at) => BitConverter.Int32BitsToSingle((int)ReadUInt32(at));
    }
}
