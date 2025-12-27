using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IcdControl.Client.Models
{
    public class TreeNode
    {
        public string Title { get; set; }
        public object? Data { get; set; }
        public List<TreeNode> Children { get; set; } = new();

        public override string ToString() => Title;
    }
}
