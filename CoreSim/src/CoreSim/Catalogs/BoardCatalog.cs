using System.Collections.Generic;
using CoreSim.Specs;

namespace CoreSim.Catalogs
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
