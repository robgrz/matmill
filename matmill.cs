﻿using System;
using System.Collections.Generic;

using CamBam.CAD;
using CamBam.Geom;

using Tree4;
using Voronoi2;

namespace Matmill
{
    enum Pocket_path_item_type
    {
        UNDEFINED = 0x00,
        SEGMENT = 0x01,
        BRANCH_ENTRY = 0x02,
        CHORD = 0x04,
        SMOOTH_CHORD = 0x08,
        SEGMENT_CHORD = 0x10,
        SPIRAL = 0x20,
        RETURN_TO_BASE = 0x40,
        DEBUG_MAT = 0x80,
    }

    class Pocket_path : List<Pocket_path_item> { }

    internal struct Vector2d
    {
        public double X;
        public double Y;

        public double Mag
        {
            get { return Math.Sqrt(X * X + Y * Y); }
        }

        public Vector2d(double x, double y)
        {
            X = x;
            Y = y;
        }

        public Vector2d(Point2F pt)
        {
            X = pt.X;
            Y = pt.Y;
        }

        public Vector2d(Point2F start, Point2F end)
        {
            X = end.X - start.X;
            Y = end.Y - start.Y;
        }

        public Vector2d(Vector2d v)
        {
            X = v.X;
            Y = v.Y;
        }

        public Vector2d Normal()
        {
            return new Vector2d(-Y, X);
        }

        public Vector2d Inverse()
        {
            return new Vector2d(-X, -Y);
        }

        public Vector2d Unit()
        {
            double mag = Mag;
            return new Vector2d(X / mag, Y / mag);
        }

        public double Det(Vector2d b)
        {
            return X * b.Y - Y * b.X;
        }

        public static explicit operator Point2F (Vector2d v)
        {
            return new Point2F(v.X, v.Y);
        }

        public static double operator * (Vector2d a, Vector2d b)
        {
            return a.X * b.X + a.Y * b.Y;
        }

        public static Vector2d operator * (Vector2d a, double d)
        {
            return new Vector2d(a.X * d, a.Y * d);
        }

        public static Vector2d operator * (double d, Vector2d a)
        {
            return a * d;
        }

        public static Vector2d operator + (Vector2d a, Vector2d b)
        {
            return new Vector2d(a.X + b.X, a.Y + b.Y);
        }

        public static Vector2d operator - (Vector2d a, Vector2d b)
        {
            return new Vector2d(a.X - b.X, a.Y - b.Y);
        }
    }

    internal struct Cubic2F
    {
        public double ax;
        public double ay;
        public double bx;
        public double by;
        public double cx;
        public double cy;
        public double dx;
        public double dy;

        public Point2F Point(double t)
        {
            return new Point2F(ax * t * t * t + bx * t * t + cx * t + dx,
                               ay * t * t * t + by * t * t + cy * t + dy);
        }

        public Cubic2F(Point2F p0, Point2F p1, Point2F p2, Point2F p3)
        {
            dx = p0.X;
            cx = 3 * p1.X - 3 * p0.X;
            bx = 3 * p2.X - 6 * p1.X + 3 * p0.X;
            ax = p3.X - 3 * p2.X + 3 * p1.X - p0.X;

            dy = p0.Y;
            cy = 3 * p1.Y - 3 * p0.Y;
            by = 3 * p2.Y - 6 * p1.Y + 3 * p0.Y;
            ay = p3.Y - 3 * p2.Y + 3 * p1.Y - p0.Y;
        }
    }

    internal struct Hermite2F
    {
        public Point2F p1;
        public Point2F p2;
        public Point2F t1;
        public Point2F t2;

        public Point2F Point(double t)
        {
            double h00 =  2 * t * t * t - 3 * t * t + 1;
            double h10 = t * t * t - 2 * t * t + t;
            double h01 = -2 * t * t * t + 3 * t * t;
            double h11 =  t * t * t - t * t;

            double x = h00 * p1.X + h10 * t1.X + h01 * p2.X + h11 * t2.X;
            double y = h00 * p1.Y + h10 * t1.Y + h01 * p2.Y + h11 * t2.Y;

            return new Point2F(x, y);
        }

        public Hermite2F(Point2F p1, Point2F t1, Point2F p2, Point2F t2)
        {
            this.p1 = p1;
            this.p2 = p2;
            this.t1 = t1;
            this.t2 = t2;
        }
    }

    internal struct Biarc2F
    {
        public Point2F p1;
        public Point2F p2;
        public Point2F pm;
        public Point2F c1;
        public Point2F c2;
        public RotationDirection arc1_dir;
        public RotationDirection arc2_dir;

        /* Adapted from http://www.ryanjuckett.com/programming/biarc-interpolation
        */

        private static Point2F calc_pm(Point2F p1, Vector2d t1, Point2F p2, Vector2d t2)
        {
            Vector2d v = new Vector2d(p2 - p1);
            Vector2d t = t1 + t2;
            double v_dot_t = v * t;
            double t1_dot_t2 = t1 * t2;

            double d2;
            double d2_denom = 2 * (1 - t1_dot_t2);

            if (d2_denom == 0)  // equal tangents
            {
                d2_denom = 4.0 * (v * t2);

                d2 = v * v / d2_denom;

                if (d2_denom == 0)  // v perpendicular to tangents
                    return p1 + (Point2F) (v * 0.5);
            }
            else    // normal case
            {
                double d2_num = -v_dot_t + Math.Sqrt( v_dot_t * v_dot_t + 2 * (1 - t1_dot_t2) * (v * v));
                d2 = d2_num / d2_denom;
            }

            return (p1 + p2 + (Point2F) (d2 * (t1 - t2))) * 0.5;
        }

        private static Point2F calc_arc(Point2F p, Vector2d t, Point2F pm)
        {
            Vector2d n = t.Normal();

            Vector2d pm_minus_p = new Vector2d(pm - p);
            double c_denom = 2 * (n * pm_minus_p);
            if (c_denom == 0)  // c1 is at infinity, replace arc with line
                return Point2F.Undefined;

            return p + (Point2F) ((pm_minus_p * pm_minus_p) / c_denom * n);
        }

        private static RotationDirection calc_dir(Point2F p, Point2F c, Vector2d t)
        {
            return new Vector2d(p - c) * t.Normal() > 0 ? RotationDirection.CW : RotationDirection.CCW;
        }

        public Biarc2F(Point2F p1, Vector2d t1, Point2F p2, Vector2d t2)
        {
            this.p1 = p1;
            this.p2 = p2;
            this.pm = calc_pm(p1, t1, p2, t2);
            this.c1 = calc_arc(p1, t1, this.pm);
            this.c2 = calc_arc(p2, t2, this.pm);

            this.arc1_dir = this.c1.IsUndefined ? RotationDirection.Unknown : calc_dir(p1, this.c1, t1);
            this.arc2_dir = this.c2.IsUndefined ? RotationDirection.Unknown : calc_dir(p2, this.c2, t2);
        }
    }

    class Pocket_path_item: Polyline
    {
        public Pocket_path_item_type Item_type;

        public Pocket_path_item() : base()
        {

        }

        public Pocket_path_item(Pocket_path_item_type type) : base()
        {
            Item_type = type;
        }

        public Pocket_path_item(Pocket_path_item_type type, int i) : base(i)
        {
            Item_type = type;
        }

        public Pocket_path_item(Pocket_path_item_type type, Polyline p) : base(p)
        {
            Item_type = type;
        }

        public void Add(Point2F pt)
        {
            base.Add(new Point3F(pt.X, pt.Y, 0));
        }

        public void Add(Curve curve)
        {
            foreach (Point2F pt in curve.Points)
                this.Add(pt);
        }

        public void Add(Cubic2F spline, double tstep)
        {
            Polyline p = new Polyline();

            double t = 0;
            while (t < 1)
            {
                p.Add((Point3F) spline.Point(t));
                t += tstep;
            }
            p.Add((Point3F)spline.Point(1.0));

            p.RemoveDuplicatePoints(0.001);

            p = p.ArcFit(0.001);

            foreach (PolylineItem pi in p.Points)
                this.Points.Add(pi);
        }

        public void Add(Hermite2F spline, double step)
        {
            // approximation
            double nsteps = spline.p1.DistanceTo(spline.p2) / step;
            nsteps = Math.Max(nsteps, 8);
            double tstep = 1.0 / nsteps;

            Polyline p = new Polyline();

            double t = 0;
            while (t < 1)
            {
                p.Add((Point3F) spline.Point(t));
                t += tstep;
            }
            p.Add((Point3F)spline.Point(1.0));

            //p.RemoveDuplicatePoints(step / 10);

            p = p.ArcFit(step / 10);

            foreach (PolylineItem pi in p.Points)
                this.Points.Add(pi);
        }

        public void Add(Biarc2F biarc, double tolerance)
        {
            if (biarc.c1.IsUndefined)
            {
                Line2F line = new Line2F(biarc.p1, biarc.pm);
                this.Add(line, tolerance);
            }
            else
            {
                Arc2F arc = new Arc2F(biarc.c1, biarc.p1, biarc.pm, biarc.arc1_dir);
                this.Add(arc, tolerance);
            }

            if (biarc.c2.IsUndefined)
            {
                Line2F line = new Line2F(biarc.pm, biarc.p2);
                this.Add(line, tolerance);
            }
            else
            {
                Arc2F arc = new Arc2F(biarc.c2, biarc.pm, biarc.p2, biarc.arc2_dir);
                this.Add(arc, tolerance);
            }
        }
    }

    class Pocket_generator
    {
        private const double VORONOI_MARGIN = 1.0;
        private const bool ANALIZE_INNER_INTERSECTIONS = false;
        private const double ENGAGEMENT_TOLERANCE_PERCENTAGE = 0.001;  // 0.1 %

        private readonly Polyline _outline;
        private readonly Polyline[] _islands;

        private readonly T4 _reg_t4;

        private double _general_tolerance = 0.001;
        private double _cutter_r = 1.5;
        private double _margin = 0;
        private double _max_engagement = 3.0 * 0.4;
        private double _min_engagement = 3.0 * 0.1;
        private double _segmented_slice_engagement_derating_k = 0.5;
        private Point2F _startpoint = Point2F.Undefined;
        private RotationDirection _dir = RotationDirection.CW;
        private Pocket_path_item_type _emit_options =    Pocket_path_item_type.BRANCH_ENTRY
                                                       | Pocket_path_item_type.CHORD
                                                       | Pocket_path_item_type.SPIRAL
                                                       | Pocket_path_item_type.SEGMENT;

        public double Cutter_d                                    { set { _cutter_r = value / 2.0;}}
        public double General_tolerance                           { set { _general_tolerance = value; } }
        public double Margin                                      { set { _margin = value; } }
        public double Max_engagement                              { set { _max_engagement = value; } }
        public double Min_engagement                              { set { _min_engagement = value; } }
        public double Segmented_slice_engagement_derating_k       { set { _segmented_slice_engagement_derating_k = value; } }
        public Pocket_path_item_type Emit_options                 { set { _emit_options = value; } }
        public Point2F Startpoint                                 { set { _startpoint = value; } }
        public RotationDirection Mill_direction                   { set { _dir = value; } }

        private RotationDirection _initial_dir
        {
            // since unknown is 'dont care', CW is ok
            get { return _dir != RotationDirection.Unknown ? _dir : RotationDirection.CW; }
        }

        private double _min_passable_mic_radius
        {
            get { return 0.1 * _cutter_r; } // 5 % of cutter diameter is seems to be ok
        }

        private bool should_emit(Pocket_path_item_type mask)
        {
            return (int)(_emit_options & mask) != 0;
        }

        private List<Point2F> sample_curve_old(Polyline p, double step)
        {
            // divide curve evenly. There is a bug in CamBam's divide by step routine (duplicate points), while 'divide to n equal segments' should work ok.
            // execution speed may be worse, but who cares
            double length = p.GetPerimeter();
            int nsegs = (int)Math.Max(Math.Ceiling(length / step), 1);

            List<Point2F> points = new List<Point2F>();
            foreach (Point3F pt in PointListUtils.CreatePointlistFromPolyline(p, nsegs).Points)
                points.Add((Point2F) pt);

            return points;
        }

        private List<Point2F> sample_curve(Polyline p, double step)
        {
            // try to to sample curve segments by step
            // also make sure to include all segments startpoints to represent sharp edges
            List<Point2F> points = new List<Point2F>();

            for (int sidx = 0; sidx < p.NumSegments; sidx++)
            {
                object seg = p.GetSegment(sidx);
                if (seg is Line2F)
                {
                    Line2F line = (Line2F)seg;

                    points.Add(line.p1);

                    double len = line.Length();
                    int npoints = (int) (len / step);

                    if (npoints <= 0)
                        continue;

                    double dx = (line.p2.X - line.p1.X) / npoints;
                    double dy = (line.p2.Y - line.p1.Y) / npoints;
                    // exclude first and last points
                    for (int i = 1; i < npoints - 1; i++)
                    {
                        double x = line.p1.X + i * dx;
                        double y = line.p1.Y + i * dy;
                        points.Add(new Point2F(x, y));
                    }
                }
                else if (seg is Arc2F)
                {
                    Arc2F arc = (Arc2F)seg;
                    points.Add(arc.P1);

                    double len = arc.GetPerimeter();
                    int npoints = (int)(len / step);

                    if (npoints <= 0)
                        continue;

                    double start = arc.Start * Math.PI / 180.0 ;
                    double da = arc.Sweep * Math.PI / (180.0  * npoints);
                    // exclude first and last points
                    for (int i = 1; i < npoints - 1; i++)
                    {
                        double x = arc.Center.X + arc.Radius * Math.Cos(start + da * i);
                        double y = arc.Center.Y + arc.Radius * Math.Sin(start + da * i);
                        points.Add(new Point2F(x, y));
                    }
                }
            }

            return points;
        }

        private bool is_line_inside_region(Line2F line, bool should_analize_inner_intersections)
        {
            if (!_outline.PointInPolyline(line.p1, _general_tolerance)) return false;     // p1 is outside of outer curve boundary
            if (!_outline.PointInPolyline(line.p2, _general_tolerance)) return false;  // p2 is outside of outer curve boundary
            if (should_analize_inner_intersections && _outline.LineIntersections(line, _general_tolerance).Length != 0) return false; // both endpoints are inside, but there are intersections, outer curve must be concave

            foreach (Polyline island in _islands)
            {
                if (island.PointInPolyline(line.p1, _general_tolerance)) return false;  // p1 is inside hole
                if (island.PointInPolyline(line.p2, _general_tolerance)) return false;  // p2 is inside hole
                if (should_analize_inner_intersections && island.LineIntersections(line, _general_tolerance).Length != 0) return false; // p1, p2 are outside hole, but there are intersections
            }
            return true;
        }

        private List<Line2F> get_mat_segments()
        {
            List<Point2F> plist = new List<Point2F>();

            plist.AddRange(sample_curve_old(_outline, _cutter_r / 10));
            foreach (Polyline p in _islands)
                plist.AddRange(sample_curve_old(p, _cutter_r / 10));

            Host.log("got {0} points", plist.Count);

            double[] xs = new double[plist.Count + 1];
            double[] ys = new double[plist.Count + 1];

            double min_x = double.MaxValue;
            double max_x = double.MinValue;
            double min_y = double.MaxValue;
            double max_y = double.MinValue;

            // HACK
            // There is a bug in Voronoi generator implementation. Sometimes it produces a completely crazy partitioning.
            // Looks like its overly sensitive to the first processed points, their count and location. If first stages
            // go ok, then everything comes nice. Beeing a Steven Fortune's algorithm, it process points by a moving sweep line.
            // Looks like the line is moving from the bottom to the top, thus sensitive points are the most bottom ones.
            // We try to cheat and add one more point so it would be the single most bottom point.
            // Then generator initially will see just one point, do a right magic and continue with a sane result :-)
            // We place this initial point under the lefmost bottom point at the sufficient distance,
            // then these two points will form a separate Voronoi cite not influencing the remaining partition.
            // Sufficient distance is defined as region width / 2 for now.

            int lb_idx = 0;

            for (int i = 0; i < plist.Count; i++)
            {
                xs[i] = plist[i].X;
                ys[i] = plist[i].Y;
                if (xs[i] < min_x) min_x = xs[i];
                if (xs[i] > max_x) max_x = xs[i];
                if (ys[i] > max_y) max_y = ys[i];

                if (ys[i] <= min_y)
                {
                    if (ys[i] < min_y)
                    {
                        min_y = ys[i];
                        lb_idx = i;  // stricly less, it's a new leftmost bottom for sure
                    }
                    else
                    {
                        if (xs[i] < xs[lb_idx])  // it's a new leftmost bottom if more lefty
                            lb_idx = i;
                    }
                }
            }

            double width = max_x - min_x;
            xs[plist.Count] = xs[lb_idx];
            ys[plist.Count] = ys[lb_idx] - width / 2;

            min_x -= VORONOI_MARGIN;
            max_x += VORONOI_MARGIN;
            min_y -= VORONOI_MARGIN + width / 2;
            max_y += VORONOI_MARGIN;

            List<GraphEdge> edges = new Voronoi(_general_tolerance).generateVoronoi(xs, ys, min_x, max_x, min_y, max_y);

            Host.log("voronoi partitioning completed. got {0} edges", edges.Count);

            List<Line2F> inner_segments = new List<Line2F>();

            foreach (GraphEdge e in edges)
            {
                Line2F seg = new Line2F(e.x1, e.y1, e.x2, e.y2);
                if (seg.Length() < double.Epsilon) continue;    // extra small segment, discard
                if (! is_line_inside_region(seg, ANALIZE_INNER_INTERSECTIONS)) continue;
                inner_segments.Add(seg);
            }

            Host.log("got {0} inner segments", inner_segments.Count);

            return inner_segments;
        }

        private double get_mic_radius(Point2F pt)
        {
            double radius = double.MaxValue;
            foreach(object item in _reg_t4.Get_nearest_objects(pt.X, pt.Y))
            {
                double dist = 0;
                if (item is Line2F)
                    ((Line2F)item).NearestPoint(pt, ref dist);
                else
                    ((Arc2F)item).NearestPoint(pt, ref dist);
                if (dist < radius)
                    radius = dist;
            }

            // account for margin in just one subrtract. Nice !
            return radius - _cutter_r - _margin;
        }

        private Slice find_prev_slice(Branch branch, Slice last_slice, Point2F pt, double radius, T4 ready_slices)
        {
            Slice best_candidate = null;

            double min_engage = double.MaxValue;

            List<Slice> candidates = branch.Get_upstream_roadblocks();
            foreach (Slice candidate in candidates)
            {
                Slice s = new Slice(candidate, pt, radius, _dir, _cutter_r, last_slice);
                if (s.Max_engagement == 0)  // no intersections
                {
                    if (s.Dist > 0)        // circles are too far away, ignore
                        continue;
                    // circles are inside each other, distance is negative, that's ok.
                    // this slice is a good candidate
                }
                else
                {
                    s.Refine(find_colliding_slices(s, ready_slices), _cutter_r, _segmented_slice_engagement_derating_k, _cutter_r);
                }


                double slice_engage = s.Max_engagement;
                if (slice_engage > _max_engagement)
                    continue;

                if (best_candidate == null || slice_engage < min_engage)
                {
                    min_engage = slice_engage;
                    best_candidate = candidate;
                }
            }

            return best_candidate;
        }

        private Slice find_nearest_slice(Branch branch, Point2F pt)
        {
            Slice best_candidate = null;

            double min_dist = double.MaxValue;

            List<Slice> candidates = branch.Get_upstream_roadblocks();
            foreach (Slice candidate in candidates)
            {
                double dist = candidate.Center.DistanceTo(pt);
                if (dist < min_dist)
                {
                    min_dist = dist;
                    best_candidate = candidate;
                }
            }

            return best_candidate;
        }

        private List<Slice> find_colliding_slices(Slice s, T4 ready_slices)
        {
            Point2F min = Point2F.Undefined;
            Point2F max = Point2F.Undefined;
            s.Get_extrema(ref min, ref max);
            T4_rect rect = new T4_rect(min.X, min.Y, max.X, max.Y);
            List<Slice> result = new List<Slice>();
            // TODO: is there a way to do it without repacking ?
            foreach (object obj in ready_slices.Get_colliding_objects(rect))
                result.Add((Slice)obj);
            return result;
        }

        // find least common ancestor for the both branches
        private Pocket_path_item switch_branch(Slice dst, Slice src, T4 ready_slices, Point2F dst_pt, Point2F src_pt)
        {
            List<Slice> path = new List<Slice>();

            Point2F current = src_pt.IsUndefined ? src.End : src_pt;
            Point2F end = dst_pt.IsUndefined ? dst.Start : dst_pt;

            Pocket_path_item p = new Pocket_path_item();

            if (dst.Parent == src)  // simple continuation
            {
                //p.Add(current);
                //p.Add(end);
                p = connect_with_smooth_chord(dst, src);
            }
            else
            {

                p.Add(current);

                List<Slice> src_ancestry = new List<Slice>();
                List<Slice> dst_ancestry = new List<Slice>();

                for (Slice s = src.Parent; s != null; s = s.Parent)
                    src_ancestry.Insert(0, s);

                for (Slice s = dst.Parent; s != null; s = s.Parent)
                    dst_ancestry.Insert(0, s);

                int lca;
                for (lca = 0; lca < Math.Min(src_ancestry.Count, dst_ancestry.Count); lca++)
                {
                    if (src_ancestry[lca] != dst_ancestry[lca])
                        break;
                }

                if (lca == 0)
                {
                    ;   // one of the slices must be the root (no ancestry). it is already included in path
                }
                else
                {
                    lca -= 1;   // the first diverging slices in ancestries were detected, lca is the last same slice, so -1
                }

                // now lca contains the lca of branches
                // collect path up from src to lca and down to dst
                for (int i = src_ancestry.Count - 1; i > lca; i--)
                    path.Add(src_ancestry[i]);

                for (int i = lca; i < dst_ancestry.Count - 1; i++)
                    path.Add(dst_ancestry[i]);

                // trace path
                // follow the path, while looking for a shortcut to reduce travel time
                // TODO: skip parts of path to reduce travel even more
                for (int i = 0; i < path.Count; i++)
                {
                    Slice s = path[i];
                    if (may_shortcut(current, end, ready_slices))
                       break;

                    current = s.Center;
                    p.Add(current);
                }

                p.Add(end);
            }

            return p;
        }

        private Pocket_path_item switch_branch(Slice dst, Slice src, T4 ready_slices)
        {
            return switch_branch(dst, src, ready_slices, Point2F.Undefined, Point2F.Undefined);
        }

        private void roll(Branch branch, T4 ready_slices, ref Slice last_slice)
        {
            Slice parent_slice = null;

            if (branch.Curve.Points.Count == 0)
                throw new Exception("branch with the empty curve");

            Point2F start_pt = branch.Curve.Start;
            double start_radius = get_mic_radius(start_pt);

            if (branch.Parent != null)
            {
                // non-initial slice
                //prev_slice = find_prev_slice(branch, last_slice, start_pt, start_radius, ready_slices);
                parent_slice = find_nearest_slice(branch, start_pt);
                if (parent_slice == null)
                {
                    Host.warn("failed to attach branch");
                    return;
                }
            }
            else
            {
                Slice s = new Slice(start_pt, start_radius, _initial_dir);
                branch.Slices.Add(s);
                insert_in_t4(ready_slices, s);
                parent_slice = s;
                last_slice = s;
            }

            double left = 0;
            while (true)
            {
                Slice candidate = null;

                double right = 1.0;

                while (true)
                {
                    double mid = (left + right) / 2;

                    Point2F pt = branch.Curve.Get_parametric_pt(mid);

                    double radius = get_mic_radius(pt);

                    if (radius < _min_passable_mic_radius)
                    {
                        right = mid;    // assuming the branch is always starting from passable mics, so it's a narrow channel and we should be more conservative, go left
                    }
                    else
                    {
                        Slice s = new Slice(parent_slice, pt, radius, _dir, _cutter_r, last_slice);

                        if (s.Max_engagement == 0)  // no intersections, two possible cases
                        {
                            if (s.Dist <= 0)        // balls are inside each other, go right
                                left = mid;
                            else
                                right = mid;        // balls are spaced too far, go left
                        }
                        else    // intersection
                        {
                            // XXX: is this candidate is better than the last ?
                            candidate = s;
                            candidate.Refine(find_colliding_slices(candidate, ready_slices), _cutter_r, _segmented_slice_engagement_derating_k, _cutter_r);

                            if (candidate.Max_engagement > _max_engagement)
                            {
                                right = mid;        // overshoot, go left
                            }
                            else if ((_max_engagement - candidate.Max_engagement) / _max_engagement > ENGAGEMENT_TOLERANCE_PERCENTAGE)
                            {
                                left = mid;         // undershoot outside the strict engagement tolerance, go right
                            }
                            else
                            {
                                left = mid;         // good slice inside the tolerance, stop search
                                break;
                            }
                        }
                    }

                    Point2F other = branch.Curve.Get_parametric_pt(left == mid ? right : left);
                    if (pt.DistanceTo(other) < _general_tolerance)
                    {
                        left = mid;                 // range has shrinked, stop search
                        break;
                    }
                }

                if (candidate == null) return;

                double err = (candidate.Max_engagement - _max_engagement) / _max_engagement;

                // discard slice if outside a little relaxed overshoot
                if (err > ENGAGEMENT_TOLERANCE_PERCENTAGE * 10)
                {
                    Host.err("failed to create slice within stepover limit. stopping slicing the branch");
                    return;
                }

                // discard slice if outside the specified min engagement
                if (candidate.Max_engagement < _min_engagement) return;

                // generate branch entry after finding the first valid slice (before populating ready slices)
                if (branch.Slices.Count == 0 && last_slice != null)
                {
                    branch.Entry = switch_branch(candidate, last_slice, ready_slices);
                    branch.Entry.Item_type = Pocket_path_item_type.BRANCH_ENTRY;
                }

                branch.Slices.Add(candidate);
                insert_in_t4(ready_slices, candidate);
                parent_slice = candidate;
                last_slice = candidate;
            }
        }

        private void attach_segments(Branch me, Segpool pool)
        {
            Point2F running_end = me.Curve.End;
            List<Point2F> followers;

            while (true)
            {
                followers = pool.Pull_follow_points(running_end);

                if (followers.Count != 1)
                    break;

                running_end = followers[0];
                me.Curve.Add(running_end);   // continuation
            }

            if (followers.Count == 0) return; // end of branch, go out

            foreach (Point2F pt in followers)
            {
                Branch b = new Branch(me);
                b.Curve.Add(running_end);
                b.Curve.Add(pt);
                attach_segments(b, pool);

                if (b.Deep_distance() > _general_tolerance) // attach only 'long enough'
                    me.Children.Add(b);
                else
                    Host.log("skipping short branch");
            }
            // prefer a shortest branch
            me.Children.Sort((a, b) => a.Deep_distance().CompareTo(b.Deep_distance()));
        }

        private Branch build_tree(List<Line2F> segments)
        {
            Segpool pool = new Segpool(segments.Count, _general_tolerance);
            Branch root = new Branch(null);
            Point2F tree_start = Point2F.Undefined;

            Host.log("analyzing segments");

            // a lot of stuff going on here.
            // segments are analyzed for mic radius from both ends. passed segmens are inserted in segpool
            // hashed by one or both endpoints. if endpoint is not hashed, segment wouldn't be followed
            // from that side, preventing formation of bad tree.
            // segments are connected later in a greedy fashion, hopefully forming a mat covering all
            // pocket.
            // simultaneously we are looking for the tree root point - automatic, as a point with the largest mic,
            // or manually, as a mat segment nearest to the user specified start point.
            if (_startpoint.IsUndefined)
            {
                // automatic startpoint, choose the start segment - the one with the largest mic
                double max_r = double.MinValue;

                foreach (Line2F line in segments)
                {
                    double r1 = get_mic_radius(line.p1);
                    double r2 = get_mic_radius(line.p2);

                    if (r1 >= _min_passable_mic_radius)
                    {
                        pool.Add(line, false);
                        if (r1 > max_r)
                        {
                            max_r = r1;
                            tree_start = line.p1;
                        }
                    }
                    if (r2 >= _min_passable_mic_radius)
                    {
                        pool.Add(line, true);
                        if (r2 > max_r)
                        {
                            max_r = r2;
                            tree_start = line.p2;
                        }
                    }
                }
            }
            else
            {
                // manual startpoint, seek the segment with the closest end to startpoint
                if (! is_line_inside_region(new Line2F(_startpoint, _startpoint), false))
                {
                    Host.warn("startpoint is outside the pocket");
                    return null;
                }
                if (get_mic_radius(_startpoint) < _min_passable_mic_radius)
                {
                    Host.warn("startpoint radius < cutter radius");
                    return null;
                }

                // insert startpoing to root poly, it would be connected to seg_start later
                root.Curve.Add(_startpoint);

                double min_dist = double.MaxValue;

                foreach (Line2F line in segments)
                {
                    double r1 = get_mic_radius(line.p1);
                    double r2 = get_mic_radius(line.p2);

                    if (r1 >= _min_passable_mic_radius)
                    {
                        pool.Add(line, false);
                        double dist = _startpoint.DistanceTo(line.p1);
                        if (dist < min_dist && is_line_inside_region(new Line2F(_startpoint, line.p1), true))
                        {
                            min_dist = dist;
                            tree_start = line.p1;
                        }
                    }
                    if (r2 >= _min_passable_mic_radius)
                    {
                        pool.Add(line, true);
                        double dist = _startpoint.DistanceTo(line.p2);
                        if (dist < min_dist && is_line_inside_region(new Line2F(_startpoint, line.p2), true))
                        {
                            min_dist = dist;
                            tree_start = line.p2;
                        }
                    }
                }
            }

            if (tree_start.IsUndefined)
            {
                Host.warn("failed to choose tree start point");
                return null;
            }

            Host.log("done analyzing segments");
            Host.log("got {0} hashes", pool.N_hashes);

            root.Curve.Add(tree_start);
            attach_segments(root, pool);
            return root;
        }

        private void insert_in_t4(T4 t4, Slice slice)
        {
            Point2F min = Point2F.Undefined;
            Point2F max = Point2F.Undefined;
            slice.Get_ball_extrema(ref min, ref max);
            T4_rect rect = new T4_rect(min.X, min.Y, max.X, max.Y);
            t4.Add(rect, slice);
        }

        private void insert_in_t4(T4 t4, Polyline p)
        {
            for (int i = 0; i < p.NumSegments; i++)
            {
                object seg = p.GetSegment(i);
                T4_rect rect;

                if (seg is Line2F)
                {
                    Line2F line = ((Line2F)seg);
                    rect = new T4_rect(Math.Min(line.p1.X, line.p2.X),
                                        Math.Min(line.p1.Y, line.p2.Y),
                                        Math.Max(line.p1.X, line.p2.X),
                                        Math.Max(line.p1.Y, line.p2.Y));
                }
                else if (seg is Arc2F)
                {
                    Point2F min = Point2F.Undefined;
                    Point2F max = Point2F.Undefined;
                    ((Arc2F)seg).GetExtrema(ref min, ref max);
                    rect = new T4_rect(min.X, min.Y, max.X, max.Y);
                }
                else
                {
                    throw new Exception("unknown segment type");
                }

                t4.Add(rect, seg);
            }
        }

        // check if it is possible to shortcut from a to b via while
        // staying inside the slice balls
        // we are collecting all the intersections and tracking the list of balls we're inside
        // at any given point. If list becomes empty, we can't shortcut
        private bool may_shortcut(Point2F a, Point2F b, List<Slice> colliders)
        {
            Line2F path = new Line2F(a, b);
            SortedList<double, List<Slice>> intersections = new SortedList<double, List<Slice>>();
            List<Slice> running_collides = new List<Slice>();

            foreach (Slice s in colliders)
            {
                Line2F insects = s.Ball.LineIntersect(path, _general_tolerance);

                if (insects.p1.IsUndefined && insects.p2.IsUndefined)
                {
                    // no intersections: check if whole path lay inside the circle
                    if (   a.DistanceTo(s.Ball.Center) < s.Ball.Radius + _general_tolerance
                        && b.DistanceTo(s.Ball.Center) < s.Ball.Radius + _general_tolerance)
                        return true;
                }
                else if (insects.p1.IsUndefined || insects.p2.IsUndefined)
                {
                    // single intersection. one of the path ends must be inside the circle, otherwise it is a tangent case
                    // and should be ignored
                    if (a.DistanceTo(s.Ball.Center) < s.Ball.Radius + _general_tolerance)
                    {
                        running_collides.Add(s);
                    }
                    else if (b.DistanceTo(s.Ball.Center) < s.Ball.Radius + _general_tolerance)
                    {
                        ;
                    }
                    else
                    {
                        continue;
                    }

                    Point2F c = insects.p1.IsUndefined ? insects.p2 : insects.p1;
                    double d = c.DistanceTo(a);
                    if (!intersections.ContainsKey(d))
                        intersections.Add(d, new List<Slice>());
                    intersections[d].Add(s);
                }
                else
                {
                    // double intersection
                    double d = insects.p1.DistanceTo(a);
                    if (! intersections.ContainsKey(d))
                        intersections.Add(d, new List<Slice>());
                    intersections[d].Add(s);

                    d = insects.p2.DistanceTo(a);
                    if (! intersections.ContainsKey(d))
                        intersections.Add(d, new List<Slice>());
                    intersections[d].Add(s);
                }
            }

            if (running_collides.Count == 0)
                return false;

            foreach (var ins in intersections)
            {
                foreach (Slice s in ins.Value)
                {
                    if (running_collides.Contains(s))
                        running_collides.Remove(s);
                    else
                        running_collides.Add(s);
                }

                if (running_collides.Count == 0 && (ins.Key + _general_tolerance < a.DistanceTo(b)))
                    return false;
            }

            return true;
        }

        private bool may_shortcut(Point2F a, Point2F b, T4 slices)
        {
            T4_rect rect = new T4_rect(Math.Min(a.X, b.X),
                                       Math.Min(a.Y, b.Y),
                                       Math.Max(a.X, b.X),
                                       Math.Max(a.Y, b.Y));

            List<Slice> colliders = new List<Slice>();
            foreach(object obj in slices.Get_colliding_objects(rect))
                colliders.Add((Slice)obj);

            return may_shortcut(a, b, colliders);
        }

        private Point2F lines_intersection(Line2F a, Line2F b)
        {
            double x1 = a.p1.X;
            double y1 = a.p1.Y;
            double x2 = a.p2.X;
            double y2 = a.p2.Y;
            double x3 = b.p1.X;
            double y3 = b.p1.Y;
            double x4 = b.p2.X;
            double y4 = b.p2.Y;

            double x = ((x1 * y2 - y1 * x2) * (x3 - x4) - (x1 - x2) * (x3 * y4 - y3 * x4)) /
                       ((x1 - x2) * (y3 - y4) - (y1 - y2) * (x3 - x4));

            double y = ((x1 * y2 - y1 * x2) * (y3 - y4) - (y1 - y2) * (x3 * y4 - y3 * x4)) /
                       ((x1 - x2) * (y3 - y4) - (y1 - y2) * (x3 - x4));

            return new Point2F(x, y);
        }

        private Point2F infinite_lines_intersection(Point2F start_a, Vector2F v_a, Point2F start_b, Vector2F v_b)
        {
            Line2F a = new Line2F(start_a.X, start_a.Y, start_a.X + v_a.X, start_a.Y + v_a.Y);
            Line2F b = new Line2F(start_b.X, start_b.Y, start_b.X + v_b.X, start_b.Y + v_b.Y);

            //return lines_intersection(a, b);
            return Line2F.ProjectionIntersect(a, b);
        }

        // connect slices with a cubic spline
        // XXX: to be tested
        private Pocket_path_item connect_with_smooth_chord(Slice slice, Slice prev_slice)
        {
            Point2F start = prev_slice.End;
            Point2F end = slice.Start;
            // unit normals to points
            Vector2d vn_start = new Vector2d(prev_slice.Center, start).Unit();
            Vector2d vn_end = new Vector2d(slice.Center, end).Unit();
            // tangents to points
            Vector2d vt_start;
            Vector2d vt_end;
            if (prev_slice.Segments[0].Direction == RotationDirection.CW)
                vt_start = new Vector2d(vn_start.Y, -vn_start.X);
            else
                vt_start = new Vector2d(-vn_start.Y, vn_start.X);

            if (slice.Segments[0].Direction == RotationDirection.CW)
                vt_end = new Vector2d(vn_end.Y, -vn_end.X);
            else
                vt_end = new Vector2d(-vn_end.Y, vn_end.X);

            Biarc2F biarc = new Biarc2F(start, vt_start, end, vt_end);

            Pocket_path_item result = new Pocket_path_item(Pocket_path_item_type.SMOOTH_CHORD);
            result.Add(biarc, _general_tolerance);
            return result;
        }

        private Pocket_path generate_path(List<Branch> traverse, T4 ready_slices)
        {
            Slice last_slice = null;

            Pocket_path path = new Pocket_path();

            Slice root_slice = traverse[0].Slices[0];

            // emit spiral toolpath for root
            if (should_emit(Pocket_path_item_type.SPIRAL))
            {
                Polyline spiral = SpiralGenerator.GenerateFlatSpiral(root_slice.Center, root_slice.Start, _max_engagement, _initial_dir);
                path.Add(new Pocket_path_item(Pocket_path_item_type.SPIRAL, spiral));
            }

            for (int bidx = 0; bidx < traverse.Count; bidx++)
            {
                Branch b = traverse[bidx];

                if (should_emit(Pocket_path_item_type.DEBUG_MAT))
                {
                    Pocket_path_item mat = new Pocket_path_item(Pocket_path_item_type.DEBUG_MAT);
                    mat.Add(b.Curve);
                    path.Add(mat);
                }

                if (should_emit(Pocket_path_item_type.BRANCH_ENTRY) && b.Entry != null)
                {
                    path.Add(b.Entry);
                }

                for (int sidx = 0; sidx < b.Slices.Count; sidx++)
                {
                    Slice s = b.Slices[sidx];

                    // connect following branch slices with chords
                    if (sidx > 0)
                    {
                        if (should_emit(Pocket_path_item_type.CHORD))
                        {
                            Pocket_path_item chord = new Pocket_path_item(Pocket_path_item_type.CHORD);
                            chord.Add(last_slice.End);
                            chord.Add(s.Start);
                            path.Add(chord);
                        }
                        else if (should_emit(Pocket_path_item_type.SMOOTH_CHORD))
                        {
                            Pocket_path_item arc = connect_with_smooth_chord(s, last_slice);
                            path.Add(arc);
                        }
                    }

                    // emit segments
                    for (int segidx = 0; segidx < s.Segments.Count; segidx++)
                    {
                        // connect segments
                        if (should_emit(Pocket_path_item_type.SEGMENT_CHORD) && segidx > 0)
                        {
                            Pocket_path_item segchord = new Pocket_path_item(Pocket_path_item_type.CHORD);
                            segchord.Add(s.Segments[segidx - 1].P2);
                            segchord.Add(s.Segments[segidx].P1);
                            path.Add(segchord);
                        }

                        if (should_emit(Pocket_path_item_type.SEGMENT))
                        {
                            Pocket_path_item slice = new Pocket_path_item(Pocket_path_item_type.SEGMENT);
                            slice.Add(s.Segments[segidx], _general_tolerance);
                            //arc.Tag = String.Format("me {0:F4}, so {1:F4}", s.Max_engagement, s.Max_engagement / (_cutter_r * 2));
                            path.Add(slice);
                        }
                    }
                    last_slice = s;
                }
            }

            if (should_emit(Pocket_path_item_type.RETURN_TO_BASE))
            {
                Pocket_path_item return_to_base = switch_branch(root_slice, last_slice, ready_slices, root_slice.Center, Point2F.Undefined);
                return_to_base.Item_type = Pocket_path_item_type.RETURN_TO_BASE;
                path.Add(return_to_base);
            }

            return path;
        }

        public Pocket_path run()
        {
            if (should_emit(Pocket_path_item_type.SMOOTH_CHORD) && should_emit(Pocket_path_item_type.CHORD))
                throw new Exception("smooth chords and straight chords are mutually exclusive");

            if (_dir == RotationDirection.Unknown && should_emit(Pocket_path_item_type.SMOOTH_CHORD))
                throw new Exception("smooth chords are not allowed for the variable mill direction");

            List<Line2F> mat_lines = get_mat_segments();

            Host.log("building tree");
            Branch root = build_tree(mat_lines);
            if (root == null)
            {
                Host.warn("failed to build tree");
                return null;
            }

            List<Branch> traverse = root.Df_traverse();

            T4 ready_slices = new T4(_reg_t4.Rect);
            Slice last_slice = null;

            Host.log("generating slices");
            foreach (Branch b in traverse)
                roll(b, ready_slices, ref last_slice);

            Host.log("generating path");
            return generate_path(traverse, ready_slices);
        }

        public Pocket_generator(Polyline outline, Polyline[] islands)
        {
            _outline = outline;
            _islands = islands;

            Point3F min = Point3F.Undefined;
            Point3F max = Point3F.Undefined;

            _outline.GetExtrema(ref min, ref max);

            _reg_t4 = new T4(new T4_rect(min.X - 1, min.Y - 1, max.X + 1, max.Y + 1));

            insert_in_t4(_reg_t4, _outline);
            foreach (Polyline island in _islands)
                insert_in_t4(_reg_t4, island);
        }
    }
}
