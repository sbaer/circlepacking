using System;
using Rhino;
using Rhino.Geometry;

namespace CirclePacking
{
  enum PackingAlgorithm
  {
    Simple = 0,
    Fast = 1,
    Double = 2,
    Random = 3
  }

  class PackCircles : IDisposable
  {
    readonly PackingCircle[] m_circles;
    Point3d m_base;
    BoundingBox m_cached_bbox;

    // center and radius of each circle is random
    public PackCircles(Point3d basePoint, int circleCount, double minRadius, double maxRadius)
    {
      m_base = new Point3d(basePoint);
      m_circles = new PackingCircle[circleCount];

      Random rnd = new Random();
      for( int i=0; i<circleCount; i++)
      {
        Point3d center = new Point3d(m_base.X + rnd.NextDouble() * minRadius, m_base.Y + rnd.NextDouble() * minRadius, m_base.Z);
        double radius = minRadius + rnd.NextDouble() * (maxRadius - minRadius);
        m_circles[i] = new PackingCircle(center, radius);
      }
      DestroyBoundingBoxCache();
      Rhino.Display.DisplayPipeline.CalculateBoundingBox += DisplayPipeline_CalculateBoundingBox;
      Rhino.Display.DisplayPipeline.PostDrawObjects += DisplayPipeline_PostDrawObjects;
    }

    void DisplayPipeline_PostDrawObjects(object sender, Rhino.Display.DrawEventArgs e)
    {
      //Draw all circles using a pipeline
      foreach (var c in m_circles)
        c.Draw(e.Display);
    }

    void DisplayPipeline_CalculateBoundingBox(object sender, Rhino.Display.CalculateBoundingBoxEventArgs e)
    {
      e.IncludeBoundingBox(this.BoundingBox());
    }

    public void Dispose()
    {
      Rhino.Display.DisplayPipeline.CalculateBoundingBox -= DisplayPipeline_CalculateBoundingBox;
      Rhino.Display.DisplayPipeline.PostDrawObjects -= DisplayPipeline_PostDrawObjects;
    }


    //Add all circles to the document
    public void Add(RhinoDoc doc)
    {
      foreach (var c in m_circles)
        c.Add(doc);
    }

    //Randomize the order of all circles
    void Jiggle()
    {
      Random rnd = new Random();
      double[] sort = new double[m_circles.Length];
      for (int i=0; i<m_circles.Length; i++)
        sort[i] = rnd.NextDouble();
      Array.Sort(sort, m_circles);
    }

    //Reorder the circles from furthest to nearest the boundingbox center
    void Sort()
    {
      double[] sort = new double[m_circles.Length];
      for( int i=0; i<m_circles.Length; i++ )
        sort[i] = -m_base.DistanceTo(m_circles[i].Center);
      Array.Sort(sort, m_circles);
    }

    //Move all circles towards the boundingbox center
    void Contract(double damping)
    {
      if (damping < 0.01)
        return;

      foreach (var c in m_circles)
      {
        Vector3d v = m_base - c.Center;
        v *= damping;
        c.Translate(v);
      }
    }


    //Perform a packing iteration
    public bool Pack(PackingAlgorithm algorithm, double damping, double tolerance)
    {
      foreach (var c in m_circles)
        c.InMotion = false;

      if (algorithm == PackingAlgorithm.Random)
        Jiggle();
      else
        Sort(); //Simple, Fast, Double

      bool rc = false;

      for (int i = 0; i <= m_circles.Length - 2; i++)
      {
        for (int j = i + 1; j < m_circles.Length; j++)
        {
          if( algorithm == PackingAlgorithm.Double )
            rc = rc | m_circles[i].DoublePack(m_circles[j], tolerance);
          else // Fast, Random, Simple
            rc = rc | m_circles[i].FastPack(m_circles[j], tolerance);
        }
      }

      if( algorithm== PackingAlgorithm.Double || algorithm == PackingAlgorithm.Fast || algorithm == PackingAlgorithm.Random)
          Contract(damping);

      DestroyBoundingBoxCache();
      return rc;
    }

    //Calculate the boundingbox of all circles
    BoundingBox BoundingBox()
    {
      if (!m_cached_bbox.IsValid)
      {
        m_cached_bbox = Rhino.Geometry.BoundingBox.Unset;
        foreach (var c in m_circles)
          m_cached_bbox.Union(c.BoundingBox);
      }
      return m_cached_bbox;
    }

    //Erase the boundingbox cache
    void DestroyBoundingBoxCache()
    {
      m_cached_bbox = Rhino.Geometry.BoundingBox.Unset;
    }
  }

  class PackingCircle
  {
    Circle m_circle;

    public PackingCircle(Point3d center, double radius)
    {
      m_circle = new Circle(center, radius);
    }

    public Point3d Center
    {
      get { return m_circle.Center; }
    }

    public BoundingBox BoundingBox
    {
      get { return m_circle.BoundingBox; }
    }

    //If the circle is in motion, it will be drawn red
    public bool InMotion { get; set; }

    public void Translate(Vector3d direction)
    {
      m_circle.Center = m_circle.Center + direction;
    }

    //Compare this circle to another circle and move this circle in case of an overlap
    public bool FastPack(PackingCircle other, double tolerance)
    {
      Point3d a = this.Center;
      Point3d b = other.Center;

      double d = (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y);
      double r = m_circle.Radius + other.m_circle.Radius;

      if (d < ((r * r) - 0.01 * tolerance))
      {
        //If the above line evaluates to TRUE, we have an overlap
        Vector3d v = new Vector3d(a.X - b.X, a.Y - b.Y, 0);
        v.Unitize();
        v *= (r - Math.Sqrt(d));

        Translate(v);
        InMotion = true;
        return true;
      }
      return false;
    }

    //Compare this circle to another circle and moves both circles in case of an overlap
    public bool DoublePack(PackingCircle other, double tolerance)
    {
      Point3d a = this.Center;
      Point3d b = other.Center;
      double d = (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y);
      double r = m_circle.Radius + other.m_circle.Radius;

      if (d < ((r * r) - 0.01 * tolerance))
      {
        //If the above line evaluates to TRUE, we have an overlap
        Vector3d v = new Vector3d(a.X - b.X, a.Y - b.Y, 0);
        v.Unitize();
        v *= 0.5 * (r - Math.Sqrt(d));

        Translate(v);
        v.Reverse();
        other.Translate(v);
        InMotion = true;
        return true;
      }
      return false;
    }


    //The ADD function adds the current circle to Rhino. If this function is called successively, it will also
    //remove the last created object. Since I now use a Conduit to display the progress, this feature is no
    //longer used, but it is handy none the less.
    Guid m_uuid;
    public bool Add(RhinoDoc doc)
    {
      doc.Objects.Delete(m_uuid, true);
      m_uuid = doc.Objects.AddCircle(m_circle);
      return true;
    }

    //A constant color used to indicate circles in motion (dark red)
    static readonly System.Drawing.Color g_motion_color = System.Drawing.Color.FromArgb(200, 0, 0);
    //Draw this circle using a Pipeline.
    public void Draw(Rhino.Display.DisplayPipeline dp)
    {
      if (InMotion)
        dp.DrawCircle(m_circle, g_motion_color, 2);
      else
        dp.DrawCircle(m_circle, System.Drawing.Color.Black);
    }
  }
}