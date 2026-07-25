using System;
using System.Collections.Generic;

namespace ICSharpCode.SharpDevelop.Editor
{
	public interface ITextMarker
	{
		int Offset { get; }
		int Length { get; }
		object? Tag { get; set; }
	}

	public interface ITextMarkerService
	{
		ITextMarker Create(int offset, int length);
	}
}
