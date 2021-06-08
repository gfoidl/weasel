using System.Linq;

namespace Weasel.SqlServer.SqlGeneration
{
    public class WhereFragment : CustomizableWhereFragment
    {
        public WhereFragment(string sql, params object[] parameters) : base(sql, "?",
            parameters.Select(x => new CommandParameter(x)).ToArray())
        {
        }
    }
}