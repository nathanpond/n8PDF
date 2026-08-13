using System.Text;

namespace n8PDF.Images;

/// <summary>
/// Reads an enhanced metafile.
/// </summary>
/// <remarks>
/// A metafile is not a picture but the record of one being drawn: a list of the commands a program
/// gave — move here, line to there, fill with this, write that — kept so they can be given again.
/// So this is an interpreter rather than a decoder. It replays the records against a graphics state
/// of pens, brushes, fonts and transforms, and what comes out is a drawing rather than pixels: the
/// same commands in a form the PDF can be written from, which is what keeps a chart sharp at any
/// size a reader cares to look at it.
///
/// What is handled is what a picture in a document is made of: paths and the shapes that are
/// shorthand for them, the pens and brushes that colour them, text, and the bitmaps a drawing can
/// carry. What is not is the rest of an interface designed to drive a screen: raster operations,
/// clipping regions and palettes.
///
/// A metafile written by anything modern carries the newer format's records as well, inside the
/// comments of this one. Where a file has them they are what draws it: that is what they are for,
/// and the old records beside them are a copy left for readers that cannot. The two are read in
/// the one order the file puts them in, since the old records are not always only a copy — a file
/// may hand the drawing back to the older interface part way through, and says so where it does,
/// after which its records draw until the newer ones resume.
/// </remarks>
public static class EmfDecoder
{
    /// <summary>A metafile begins with a header record and the signature " EMF".</summary>
    public static bool IsEmf(byte[] data) =>
        data.Length > 44 &&
        data[0] == 0x01 && data[1] == 0 && data[2] == 0 && data[3] == 0 &&
        data[40] == 0x20 && data[41] == 0x45 && data[42] == 0x4D && data[43] == 0x46;

    public static ImageData Decode(byte[] data)
    {
        if (!IsEmf(data)) throw new ImageFormatException("Not an enhanced metafile.");

        var drawing = new Interpreter(data, HasEmfPlus(data), Units(data)).Run();

        if (drawing.Operations.Count == 0)
            throw new ImageFormatException("The metafile draws nothing this can read.");

        // A drawing has no pixels of its own. The size stands for its natural size in points, so
        // that a document which asks for no size in particular still gets its proportions.
        return new ImageData(
            Math.Max(1, (int)Math.Round(drawing.Width)),
            Math.Max(1, (int)Math.Round(drawing.Height)),
            [], ImageEncoding.Raw, ImageColorSpace.Rgb)
        {
            Drawing = drawing
        };
    }

    /// <summary>
    /// Whether the file carries the newer format's records at all, which decides which of the two
    /// draws it. They travel inside the comments of the old, and a comment that opens with the
    /// letters "EMF+" is not a comment.
    /// </summary>
    private static bool HasEmfPlus(byte[] data)
    {
        var at = 0;

        while (at + 8 <= data.Length)
        {
            var type = (uint)(data[at] | (data[at + 1] << 8) | (data[at + 2] << 16) | (data[at + 3] << 24));
            var size = data[at + 4] | (data[at + 5] << 8) | (data[at + 6] << 16) | (data[at + 7] << 24);

            if (size < 8 || at + size > data.Length) break;

            if (type == 70 && at + 16 <= data.Length)
            {
                var length = data[at + 8] | (data[at + 9] << 8) | (data[at + 10] << 16) | (data[at + 11] << 24);

                if (EmfPlusInterpreter.IsEmfPlusComment(data, at + 12, length)) return true;
            }

            if (type == 14) break;

            at += size;
        }

        return false;
    }

    /// <summary>How many of its own units a metafile is across, which is what its bounds say.</summary>
    private static int Units(byte[] data)
    {
        var left = data[8] | (data[9] << 8) | (data[10] << 16) | (data[11] << 24);
        var right = data[16] | (data[17] << 8) | (data[18] << 16) | (data[19] << 24);

        return Math.Max(1, right - left + 1);
    }

    /// <summary>
    /// Replays a metafile's records.
    /// </summary>
    /// <param name="hasPlus">
    /// Whether the file carries the newer format's records. Where it does, they draw it, and the
    /// old records draw only what the file hands back to them.
    /// </param>
    /// <param name="units">How many of its own units the drawing is across.</param>
    private sealed class Interpreter(byte[] data, bool hasPlus, int units)
    {
        private readonly List<DrawingOperation> _operations = [];

        /// <summary>The newer format's records, read as the comments carrying them come by.</summary>
        private EmfPlusInterpreter.Reader? _plus;

        /// <summary>Whether the newer records have handed the drawing back to these ones.</summary>
        private bool _handedBack;
        private readonly Dictionary<uint, object> _objects = [];
        private readonly Stack<State> _saved = new();

        private State _state = new();
        private List<PathStep> _path = [];
        private bool _recording;
        private (double X, double Y) _pen;
        private (double X, double Y) _start;

        // The metafile's own coordinates, and what they have to be multiplied by to become points.
        private double _scale = 1;
        private double _left;
        private double _top;

        private double _width;
        private double _height;

        /// <summary>What a metafile draws with, all of which the records change as they go.</summary>
        private sealed record State
        {
            public DrawingColor? Stroke { get; init; } = new(0, 0, 0);

            public double StrokeWidth { get; init; } = 1;

            public DrawingColor? Fill { get; init; } = new(255, 255, 255);

            public DrawingColor TextColor { get; init; }

            public bool EvenOdd { get; init; }

            public LogicalFont Font { get; init; } = new("Helvetica", 12, false, false, 0);

            public double ScaleX { get; init; } = 1;

            public double ScaleY { get; init; } = 1;
        }

        private sealed record LogicalFont(string Family, double Size, bool Bold, bool Italic, double Angle);

        public VectorDrawing Run()
        {
            ReadHeader();

            // What the newer records measure in: the same units the bounds are given in, which the
            // header has just said how to turn into points.
            if (hasPlus) _plus = EmfPlusInterpreter.Begin(_width / Math.Max(1, units), _operations);

            var at = 0;

            while (at + 8 <= data.Length)
            {
                var type = ReadUInt32(at);
                var size = (int)ReadUInt32(at + 4);

                if (size < 8 || at + size > data.Length) break;

                Record(type, at, size);

                if (type == 14) break; // EOF

                at += size;
            }

            return new VectorDrawing(_width, _height, _operations);
        }

        /// <summary>
        /// The header says where the drawing lies and how big it is. The bounds are in the
        /// metafile's own units and the frame is the same rectangle in hundredths of a millimetre,
        /// which between them give the size in points and what to multiply by to get there.
        /// </summary>
        private void ReadHeader()
        {
            var boundsLeft = ReadInt32(8);
            var boundsTop = ReadInt32(12);
            var boundsRight = ReadInt32(16);
            var boundsBottom = ReadInt32(20);

            var frameLeft = ReadInt32(24);
            var frameTop = ReadInt32(28);
            var frameRight = ReadInt32(32);
            var frameBottom = ReadInt32(36);

            _left = boundsLeft;
            _top = boundsTop;

            var units = Math.Max(1, boundsRight - boundsLeft + 1);
            var rows = Math.Max(1, boundsBottom - boundsTop + 1);

            // A hundredth of a millimetre is 72/2540 of a point.
            _width = Math.Max(1, (frameRight - frameLeft) * 72.0 / 2540.0);
            _height = Math.Max(1, (frameBottom - frameTop) * 72.0 / 2540.0);

            _scale = _width / units;

            // A frame that says nothing useful leaves the drawing at its own units, read as points.
            if (double.IsNaN(_scale) || _scale <= 0)
            {
                _scale = 1;
                _width = units;
                _height = rows;
            }
            else
            {
                var vertical = _height / rows;

                // The two axes are the same scale in every metafile worth reading; where they are
                // not, the taller one decides so that nothing is cut off.
                _scale = Math.Min(_scale, vertical);
                _width = units * _scale;
                _height = rows * _scale;
            }
        }

        /// <summary>
        /// Keeps a drawing the old records make. Where the file has newer ones they are what draws
        /// it, and these draw only between the record that hands the drawing back to them and the
        /// next of the newer records.
        /// </summary>
        private void Add(DrawingOperation operation)
        {
            if (!hasPlus || _handedBack) _operations.Add(operation);
        }

        /// <summary>
        /// A comment that is not one: the newer records, which are read at the point the file puts
        /// them rather than gathered up and read after, so that a drawing made of both is made in
        /// the order it was drawn.
        /// </summary>
        private void Comment(int at, int size)
        {
            if (_plus is null) return;

            var length = ReadInt32(at + 8);

            if (!EmfPlusInterpreter.IsEmfPlusComment(data, at + 12, length)) return;

            _plus.Feed(data, at + 16, Math.Min(at + 12 + length, at + size));
            _handedBack = _plus.HandedBack;
        }

        private void Record(uint type, int at, int size)
        {
            switch (type)
            {
                case 70: // COMMENT
                    Comment(at, size);
                    break;

                // ----- the state -----

                case 17: // SETMAPMODE
                case 18: // SETBKMODE
                case 22: // SETROP2
                case 23: // SETSTRETCHBLTMODE
                case 24: // SETTEXTALIGN
                    break;

                case 19: // SETPOLYFILLMODE
                    _state = _state with { EvenOdd = ReadUInt32(at + 8) == 1 };
                    break;

                case 25: // SETTEXTCOLOR
                    _state = _state with { TextColor = Color(ReadUInt32(at + 8)) };
                    break;

                case 33: // SAVEDC
                    _saved.Push(_state);
                    break;

                case 34: // RESTOREDC
                    if (_saved.Count > 0) _state = _saved.Pop();
                    break;

                case 35: // SETWORLDTRANSFORM
                case 36: // MODIFYWORLDTRANSFORM
                {
                    // Only the scale of it is taken. A drawing that rotates its world is rare, and
                    // half-applying a transform is worse than leaving it.
                    var scaleX = ReadFloat(at + 8);
                    var scaleY = ReadFloat(at + 20);

                    if (scaleX != 0 && scaleY != 0 && !double.IsNaN(scaleX) && !double.IsNaN(scaleY))
                        _state = _state with { ScaleX = scaleX, ScaleY = scaleY };

                    break;
                }

                // ----- the objects -----

                case 37: // SELECTOBJECT
                    Select(ReadUInt32(at + 8));
                    break;

                case 38: // CREATEPEN
                {
                    var handle = ReadUInt32(at + 8);
                    var style = ReadUInt32(at + 12);
                    var width = ReadInt32(at + 16);

                    _objects[handle] = new Pen(
                        style == 5 ? null : Color(ReadUInt32(at + 24)), Math.Max(0, width));

                    break;
                }

                case 39: // CREATEBRUSHINDIRECT
                {
                    var handle = ReadUInt32(at + 8);
                    var style = ReadUInt32(at + 12);

                    // Style one is the hollow brush, which fills nothing at all.
                    _objects[handle] = new Brush(style == 1 ? null : Color(ReadUInt32(at + 16)));
                    break;
                }

                case 40: // DELETEOBJECT
                    _objects.Remove(ReadUInt32(at + 8));
                    break;

                case 82: // EXTCREATEFONTINDIRECTW
                    _objects[ReadUInt32(at + 8)] = ReadFont(at + 12);
                    break;

                case 95: // EXTCREATEPEN
                {
                    var handle = ReadUInt32(at + 8);
                    var style = ReadUInt32(at + 28);
                    var width = (int)ReadUInt32(at + 32);

                    _objects[handle] = new Pen(
                        (style & 0xff) == 5 ? null : Color(ReadUInt32(at + 40)), Math.Max(0, width));

                    break;
                }

                // ----- paths -----

                case 59: // BEGINPATH
                    _recording = true;
                    _path = [];
                    break;

                case 60: // ENDPATH
                    _recording = false;
                    break;

                case 61: // CLOSEFIGURE
                    _path.Add(new PathStep(PathStepKind.Close, []));
                    break;

                case 62: // FILLPATH
                    Emit(_path, fill: true, stroke: false);
                    _path = [];
                    break;

                case 63: // STROKEANDFILLPATH
                    Emit(_path, fill: true, stroke: true);
                    _path = [];
                    break;

                case 64: // STROKEPATH
                    Emit(_path, fill: false, stroke: true);
                    _path = [];
                    break;

                case 27: // MOVETOEX
                    _pen = Point(at + 8);
                    _start = _pen;
                    _path.Add(new PathStep(PathStepKind.Move, [_pen]));
                    break;

                case 54: // LINETO
                    _pen = Point(at + 8);
                    _path.Add(new PathStep(PathStepKind.Line, [_pen]));
                    Flush();
                    break;

                case 2: // POLYGON
                case 3: // POLYLINE
                case 4: // POLYBEZIERTO
                case 5: // POLYLINETO
                case 1: // POLYBEZIER
                case 87: // POLYGON16
                case 88: // POLYLINE16
                case 89: // POLYBEZIERTO16
                case 90: // POLYLINETO16
                case 85: // POLYBEZIER16
                case 86: // POLYGON16 (the other numbering)
                    Poly(type, at, size);
                    break;

                case 8: // POLYPOLYGON
                case 91: // POLYPOLYGON16
                    PolyPolygon(at, size, type == 91);
                    break;

                case 43: // RECTANGLE
                {
                    var (x0, y0) = Point(at + 8);
                    var (x1, y1) = Point(at + 16);

                    Emit(
                        [
                            new PathStep(PathStepKind.Move, [(x0, y0)]),
                            new PathStep(PathStepKind.Line, [(x1, y0)]),
                            new PathStep(PathStepKind.Line, [(x1, y1)]),
                            new PathStep(PathStepKind.Line, [(x0, y1)]),
                            new PathStep(PathStepKind.Close, [])
                        ],
                        fill: true, stroke: true);

                    break;
                }

                case 42: // ELLIPSE
                {
                    var (x0, y0) = Point(at + 8);
                    var (x1, y1) = Point(at + 16);

                    Emit(Ellipse(x0, y0, x1, y1), fill: true, stroke: true);
                    break;
                }

                case 44: // ROUNDRECT
                {
                    // Drawn as its rectangle: the corners are a nicety, and a chart's frame reads
                    // the same either way.
                    var (x0, y0) = Point(at + 8);
                    var (x1, y1) = Point(at + 16);

                    Emit(
                        [
                            new PathStep(PathStepKind.Move, [(x0, y0)]),
                            new PathStep(PathStepKind.Line, [(x1, y0)]),
                            new PathStep(PathStepKind.Line, [(x1, y1)]),
                            new PathStep(PathStepKind.Line, [(x0, y1)]),
                            new PathStep(PathStepKind.Close, [])
                        ],
                        fill: true, stroke: true);

                    break;
                }

                // ----- text -----

                case 83: // EXTTEXTOUTA
                case 84: // EXTTEXTOUTW
                    Text(at, size, type == 84);
                    break;

                // ----- pictures -----

                case 81: // STRETCHDIBITS
                    StretchDiBits(at, size);
                    break;
            }
        }

        private sealed record Pen(DrawingColor? Color, double Width);

        private sealed record Brush(DrawingColor? Color);

        private void Select(uint handle)
        {
            // The stock objects are named by handle rather than made: the high bit says so.
            if ((handle & 0x80000000) != 0)
            {
                _state = (handle & 0x7fffffff) switch
                {
                    0 => _state with { Fill = new DrawingColor(255, 255, 255) },   // white brush
                    1 => _state with { Fill = new DrawingColor(192, 192, 192) },   // light grey
                    2 => _state with { Fill = new DrawingColor(128, 128, 128) },   // grey
                    3 => _state with { Fill = new DrawingColor(64, 64, 64) },      // dark grey
                    4 => _state with { Fill = new DrawingColor(0, 0, 0) },         // black brush
                    5 => _state with { Fill = null },                              // hollow brush
                    6 => _state with { Stroke = new DrawingColor(255, 255, 255) }, // white pen
                    7 => _state with { Stroke = new DrawingColor(0, 0, 0) },       // black pen
                    8 => _state with { Stroke = null },                            // null pen
                    _ => _state
                };

                return;
            }

            if (!_objects.TryGetValue(handle, out var found)) return;

            _state = found switch
            {
                Pen pen => _state with { Stroke = pen.Color, StrokeWidth = pen.Width },
                Brush brush => _state with { Fill = brush.Color },
                LogicalFont font => _state with { Font = font },
                _ => _state
            };
        }

        private LogicalFont ReadFont(int at)
        {
            // A logical font's height is in the metafile's own units, and is negative where it
            // names the height of the characters rather than of the whole line.
            var height = Math.Abs(ReadInt32(at)) * _scale * _state.ScaleY;
            var escapement = ReadInt32(at + 8);
            var weight = ReadInt32(at + 16);
            var italic = data[at + 20] != 0;

            var name = new StringBuilder();

            for (var i = 0; i < 32; i++)
            {
                var character = (char)ReadUInt16(at + 28 + i * 2);
                if (character == 0) break;

                name.Append(character);
            }

            return new LogicalFont(
                name.Length > 0 ? name.ToString() : "Helvetica",
                height > 0 ? height : 12,
                weight >= 600,
                italic,
                escapement / 10.0);
        }

        private void Text(int at, int size, bool wide)
        {
            // The record holds a reference rectangle, then where the text goes, then how many
            // characters it is and where in the record they are.
            var (x, y) = Point(at + 36);
            var characters = (int)ReadUInt32(at + 44);
            var offset = (int)ReadUInt32(at + 48);

            if (characters <= 0 || offset <= 0 || at + offset >= data.Length) return;

            var text = new StringBuilder(characters);

            for (var i = 0; i < characters; i++)
            {
                var index = at + offset + (wide ? i * 2 : i);
                if (index + (wide ? 1 : 0) >= data.Length) break;

                var character = wide ? (char)ReadUInt16(index) : (char)data[index];
                if (character == 0) break;

                text.Append(character);
            }

            if (text.Length == 0) return;

            var font = _state.Font;

            Add(new TextOperation(
                text.ToString(), x, y, font.Family, font.Size, font.Bold, font.Italic,
                _state.TextColor, font.Angle));
        }

        private void StretchDiBits(int at, int size)
        {
            // Where it goes and how big it is are not the bounds the record opens with: those come
            // after the source rectangle, and the size after everything else.
            var (x, y) = Point(at + 24);
            var width = ReadInt32(at + 72) * _scale * _state.ScaleX;
            var height = ReadInt32(at + 76) * _scale * _state.ScaleY;

            var headerAt = (int)ReadUInt32(at + 48);
            var headerSize = (int)ReadUInt32(at + 52);
            var bitsAt = (int)ReadUInt32(at + 56);
            var bitsSize = (int)ReadUInt32(at + 60);

            if (headerAt <= 0 || bitsAt <= 0 || headerSize <= 0 || bitsSize <= 0) return;
            if (at + bitsAt + bitsSize > data.Length) return;

            // A device-independent bitmap is a bitmap without its file header, so one is put back
            // in front of it and the same reader used.
            var file = new byte[14 + headerSize + bitsSize];

            file[0] = (byte)'B';
            file[1] = (byte)'M';

            Write(file, 2, file.Length);
            Write(file, 10, 14 + headerSize);

            Array.Copy(data, at + headerAt, file, 14, headerSize);
            Array.Copy(data, at + bitsAt, file, 14 + headerSize, bitsSize);

            try
            {
                Add(new ImageOperation(BmpDecoder.Decode(file), x, y, width, height));
            }
            catch (ImageFormatException)
            {
                // A picture this cannot read costs its own place in the drawing, not the drawing.
            }
        }

        private static void Write(byte[] target, int at, int value)
        {
            target[at] = (byte)value;
            target[at + 1] = (byte)(value >> 8);
            target[at + 2] = (byte)(value >> 16);
            target[at + 3] = (byte)(value >> 24);
        }

        private void Poly(uint type, int at, int size)
        {
            var small = type is >= 85 and <= 90;
            var count = (int)ReadUInt32(at + 24);
            var start = at + 28;

            if (count <= 0) return;

            var points = new List<(double X, double Y)>(count);

            for (var i = 0; i < count; i++)
            {
                var index = start + i * (small ? 4 : 8);
                if (index + (small ? 3 : 7) >= data.Length) return;

                points.Add(small ? SmallPoint(index) : Point(index));
            }

            var bezier = type is 1 or 4 or 85 or 89;
            var polygon = type is 2 or 86 or 87;
            var continuing = type is 4 or 5 or 89 or 90;

            var steps = new List<PathStep>();

            if (!continuing) steps.Add(new PathStep(PathStepKind.Move, [points[0]]));

            if (bezier)
            {
                // Curves come in threes: two controls and the point they arrive at.
                for (var i = continuing ? 0 : 1; i + 2 < points.Count; i += 3)
                    steps.Add(new PathStep(PathStepKind.Curve, [points[i], points[i + 1], points[i + 2]]));
            }
            else
            {
                for (var i = continuing ? 0 : 1; i < points.Count; i++)
                    steps.Add(new PathStep(PathStepKind.Line, [points[i]]));
            }

            if (polygon) steps.Add(new PathStep(PathStepKind.Close, []));

            _pen = points[^1];

            if (_recording)
            {
                _path.AddRange(steps);
                return;
            }

            // Outside a path, a polygon is filled and lined and a polyline is only lined.
            Emit(steps, polygon, stroke: true);
        }

        private void PolyPolygon(int at, int size, bool small)
        {
            var polygons = (int)ReadUInt32(at + 24);
            var points = (int)ReadUInt32(at + 28);

            if (polygons <= 0 || points <= 0) return;

            var counts = new int[polygons];
            for (var i = 0; i < polygons; i++) counts[i] = (int)ReadUInt32(at + 32 + i * 4);

            var start = at + 32 + polygons * 4;
            var steps = new List<PathStep>();
            var read = 0;

            foreach (var count in counts)
            {
                for (var i = 0; i < count; i++, read++)
                {
                    var index = start + read * (small ? 4 : 8);
                    if (index + (small ? 3 : 7) >= data.Length) return;

                    var point = small ? SmallPoint(index) : Point(index);

                    steps.Add(new PathStep(i == 0 ? PathStepKind.Move : PathStepKind.Line, [point]));
                }

                steps.Add(new PathStep(PathStepKind.Close, []));
            }

            if (_recording)
            {
                _path.AddRange(steps);
                return;
            }

            Emit(steps, fill: true, stroke: true);
        }

        /// <summary>
        /// A line drawn outside a path stands on its own, and is drawn as soon as it is met.
        /// </summary>
        private void Flush()
        {
            if (_recording || _path.Count < 2) return;

            Emit(_path, fill: false, stroke: true);
            _path = [new PathStep(PathStepKind.Move, [_pen])];
        }

        private static List<PathStep> Ellipse(double x0, double y0, double x1, double y1)
        {
            // Four curves, with the control points at the distance that makes a circle out of them.
            const double k = 0.5522847498;

            var cx = (x0 + x1) / 2;
            var cy = (y0 + y1) / 2;
            var rx = (x1 - x0) / 2;
            var ry = (y1 - y0) / 2;

            return
            [
                new PathStep(PathStepKind.Move, [(cx + rx, cy)]),
                new PathStep(PathStepKind.Curve,
                    [(cx + rx, cy + ry * k), (cx + rx * k, cy + ry), (cx, cy + ry)]),
                new PathStep(PathStepKind.Curve,
                    [(cx - rx * k, cy + ry), (cx - rx, cy + ry * k), (cx - rx, cy)]),
                new PathStep(PathStepKind.Curve,
                    [(cx - rx, cy - ry * k), (cx - rx * k, cy - ry), (cx, cy - ry)]),
                new PathStep(PathStepKind.Curve,
                    [(cx + rx * k, cy - ry), (cx + rx, cy - ry * k), (cx + rx, cy)]),
                new PathStep(PathStepKind.Close, [])
            ];
        }

        private void Emit(List<PathStep> steps, bool fill, bool stroke)
        {
            if (steps.Count == 0) return;

            var fillColor = fill ? _state.Fill : null;
            var strokeColor = stroke ? _state.Stroke : null;

            if (fillColor is null && strokeColor is null) return;

            Add(new PathOperation(
                [.. steps], fillColor, strokeColor,
                Math.Max(0.24, _state.StrokeWidth * _scale), _state.EvenOdd));
        }

        /// <summary>A point of the metafile's own, in the drawing's points.</summary>
        private (double X, double Y) Point(int at) =>
            ((ReadInt32(at) - _left) * _scale * _state.ScaleX,
                (ReadInt32(at + 4) - _top) * _scale * _state.ScaleY);

        private (double X, double Y) SmallPoint(int at) =>
            ((ReadInt16(at) - _left) * _scale * _state.ScaleX,
                (ReadInt16(at + 2) - _top) * _scale * _state.ScaleY);

        /// <summary>A colour, which a metafile writes as red, green and blue in that order.</summary>
        private static DrawingColor Color(uint value) =>
            new((byte)(value & 0xff), (byte)((value >> 8) & 0xff), (byte)((value >> 16) & 0xff));

        private uint ReadUInt32(int at) =>
            at + 3 < data.Length
                ? (uint)(data[at] | (data[at + 1] << 8) | (data[at + 2] << 16) | (data[at + 3] << 24))
                : 0;

        private int ReadInt32(int at) => (int)ReadUInt32(at);

        private int ReadUInt16(int at) => at + 1 < data.Length ? data[at] | (data[at + 1] << 8) : 0;

        private short ReadInt16(int at) => (short)ReadUInt16(at);

        private float ReadFloat(int at) => BitConverter.Int32BitsToSingle(ReadInt32(at));
    }
}
