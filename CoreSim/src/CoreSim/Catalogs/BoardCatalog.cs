using System.Collections.Generic;
using RobotTwin.CoreSim.Specs; // Assuming BoardSpec is/will be here

namespace RobotTwin.CoreSim.Catalogs
{
    public class BoardCatalog
    {
        public List<BoardSpec> Boards { get; set; } = new List<BoardSpec>();

        public BoardSpec? Find(string id)
        {
            return Boards.Find(b => b.ID == id);
        }
    }
}
