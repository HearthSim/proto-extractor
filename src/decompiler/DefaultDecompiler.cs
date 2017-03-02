using System;
using System.Collections.Generic;

namespace protoextractor.decompiler
{
	abstract class DefaultDecompiler<T>
	{
		/*
		    The decompiler is responsible for extracting input details and converting
		    these into IR.
		*/

		// The one thing this decompiler must process.
		protected T _subject;

		// The constructor forces the decompiler to take a subject.
		public DefaultDecompiler(T target)
		{
			_subject = target;
		}

		// Actual decompilation of the subject.
		// 'references' will contain a list of all types, represented by T, that were
		// referenced by this type.
		abstract public void Decompile(out List<T> references);
		// Returns an IRCLass object from a certain something.
		abstract public IR.IRClass ConstructIRClass();
		// Returns an IREnum object from a certain something.
		abstract public IR.IREnum ConstructIREnum();
	}

	// Specific exception thrown by the extractor
	public class ExtractionException : Exception
	{
		// General issue
		public ExtractionException()
		{
		}

		public ExtractionException(string message) : base(message)
		{

		}

		public ExtractionException(string message, Exception other) : base(message, other)
		{ }
	}
}
