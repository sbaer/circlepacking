using Rhino;
using Rhino.Commands;
using Rhino.Geometry;
using Rhino.Input;
using Rhino.Input.Custom;
using System.Runtime.InteropServices;

namespace CirclePacking
{
  [Guid("dac01534-a3aa-4328-a360-5095ce349116")]
  public class CirclePackingCommand : Command
  {
    ///<returns>The command name as it appears on the Rhino command line.</returns>
    public override string EnglishName { get { return "CirclePackingCommand"; } }

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
      var pack_algorithm = PackingAlgorithm.Fast;
      Point3d base_point = new Point3d();
      var option_count = new OptionInteger(100, true, 2);
      var option_min_radius = new OptionDouble(0.1, true, 0.001);
      var option_max_radius = new OptionDouble(1.0, true, 0.001);
      var option_iterations = new OptionInteger(10000, false, 100);

      bool done_looping = false;
      while (!done_looping)
      {
        var gp = new GetPoint();
        gp.SetCommandPrompt("Center of fitting solution");
        gp.AddOptionInteger("Count", ref option_count);
        gp.AddOptionDouble("MinRadius", ref option_min_radius);
        gp.AddOptionDouble("MaxRadius", ref option_max_radius);
        gp.AddOptionInteger("IterationLimit", ref option_iterations);
        int index_option_packing = gp.AddOption("Packing", pack_algorithm.ToString());
        gp.AcceptNumber(true, true);

        switch( gp.Get() )
        {
          case GetResult.Point:
            base_point = gp.Point();
            done_looping = true;
            break;
          case GetResult.Option:
            if (index_option_packing == gp.OptionIndex())
            {
              var get_algorithm = new GetOption();
              get_algorithm.SetCommandPrompt("Packing");
              get_algorithm.SetDefaultString(pack_algorithm.ToString());
              var opts = new string[]{"Fast", "Double", "Random", "Simple"};
              int current_index = 0;
              switch(pack_algorithm)
              {
                case PackingAlgorithm.Fast:
                  current_index = 0;
                  break;
                case PackingAlgorithm.Double:
                  current_index = 1;
                  break;
                case PackingAlgorithm.Random:
                  current_index = 2;
                  break;
                case PackingAlgorithm.Simple:
                  current_index = 3;
                  break;
              }
              int index_list = get_algorithm.AddOptionList("algorithm", opts, current_index);
              get_algorithm.AddOption("Help");
              while( get_algorithm.Get() == GetResult.Option )
              {
                if (index_list == get_algorithm.OptionIndex())
                {
                  int index = get_algorithm.Option().CurrentListOptionIndex;
                  if (0 == index)
                    pack_algorithm = PackingAlgorithm.Fast;
                  if (1 == index)
                    pack_algorithm = PackingAlgorithm.Double;
                  if (2 == index)
                    pack_algorithm = PackingAlgorithm.Simple;
                  if (3 == index)
                    pack_algorithm = PackingAlgorithm.Random;
                  break;
                }
                // if we get here, the user selected help
                const string help =
                  @"Fast: fast packing prevents collisions by moving one
circle away from all its intersectors. After every collision
iteration, all circles are moved towards the centre of the
packing to reduce the amount of wasted space. Collision
detection proceeds from the center outwards.

Double: similar to Fast, except that both circles are moved
in case of a collision.

Random: similar to Fast, except that collision detection is
randomized rather than sorted.

Simple: similar to Fast, but without a contraction pass
after every collision iteration.";
                Rhino.UI.Dialogs.ShowMessageBox(help, "Packing algorithm description", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Information);
              }
            }
            break;
          default:
            return Result.Cancel;
        }
      }
      int count = option_count.CurrentValue;
      double min_radius = option_min_radius.CurrentValue;
      double max_radius = option_max_radius.CurrentValue;
      int iterations = option_iterations.CurrentValue;

      // TODO: try setting up a background worker thread and
      // communicate with the GetString through messages
      //GetString gs = new GetString();
      //gs.SetCommandPrompt("Press escape to cancel");
      
      using (var all_circles = new PackCircles(base_point, count, min_radius, max_radius))
      {
        double damping = 0.1;
        for (int i = 1; i <= iterations; i++)
        {
          RhinoApp.SetCommandPrompt(string.Format("Performing circle packing iteration {0}...  (Press Shift+Ctrl to abort)", i));

          if (System.Windows.Forms.Control.ModifierKeys == (System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Shift))
          {
            RhinoApp.WriteLine("Circle fitting process aborted at iteration {0}...", i);
            break;
          }

          if (!all_circles.Pack(pack_algorithm, damping, doc.ModelAbsoluteTolerance))
          {
            RhinoApp.WriteLine("Circle fitting process completed at iteration {0}...", i);
            break;
          }

          damping *= 0.98;
          doc.Views.Redraw();
          RhinoApp.Wait();
        }
        all_circles.Add(doc);
      }
      doc.Views.Redraw();
      return Result.Success;
    }
  }
}
