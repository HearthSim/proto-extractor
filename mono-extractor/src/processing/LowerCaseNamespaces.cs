using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace protoextractor.processing
{
	class LowerCaseNamespaces : DefaultProcessor
	{
		public LowerCaseNamespaces(IR.IRProgram program) : base(program) { }

		public override IR.IRProgram Process()
		{
			foreach (var ns in _program.Namespaces)
			{
				ns.FullName = ns.FullName.ToLower();
				ns.ShortName = ns.ShortName.ToLower();
			}

			return _program;
		}
	}
}
