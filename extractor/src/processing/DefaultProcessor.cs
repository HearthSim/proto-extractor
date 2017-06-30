using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using protoextractor.IR;

namespace protoextractor.processing
{
	abstract class DefaultProcessor
	{
		// The collection of types given to this processor.
		protected IR.IRProgram _program;

		protected DefaultProcessor(IR.IRProgram program)
		{
			if (program == null)
			{
				throw new Exception("Program must be an instance");
			}

			_program = program;
		}

		// Needs to be implemented by the processor.
		public abstract IRProgram Process();
	}
}
