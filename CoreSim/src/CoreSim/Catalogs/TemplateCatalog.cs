using System.Collections.Generic;
using RobotTwin.CoreSim.Specs;

namespace RobotTwin.CoreSim.Catalogs
{
    public class TemplateCatalog
    {
        public List<TemplateSpec> Templates { get; set; } = new List<TemplateSpec>();

        public TemplateSpec? Find(string id)
        {
            return Templates.Find(t => t.TemplateId == id);
        }

        public void Register(TemplateSpec template)
        {
            if (Find(template.TemplateId) == null)
            {
                Templates.Add(template);
            }
        }
    }
}
