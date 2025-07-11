﻿using System.Diagnostics.CodeAnalysis;

namespace Nucleus.ModelEditor
{
	public struct EditorResult
	{
		public bool Succeeded;
		public string? Reason;

		public static EditorResult OK => new(true, null);
		public static EditorResult NotApplicable => new(false, null);

		/// <summary>
		/// No error occured and the operation completed successfully.
		/// </summary>
		public EditorResult() {
			Succeeded = true;
			Reason = null;
		}

		/// <summary>
		/// An error occurred; type the error reason.
		/// </summary>
		/// <param name="reason">An error occurred; type the error reason.</param>
		public EditorResult(string? reason = null) {
			Succeeded = false;
			Reason = reason;
		}

		/// <summary>
		/// Custom logic where succeeded can be true/false and reason can be provided/excluded separately.
		/// </summary>
		/// <param name="succeeded"></param>
		/// <param name="reason"></param>
		public EditorResult(bool succeeded, string? reason = null) {
			Succeeded = succeeded;
			Reason = reason;
		}
	}
	public struct EditorReturnResult<T>
	{
		public T? Result;
		public string? Reason;

		[MemberNotNullWhen(false, nameof(Result))]
		[MemberNotNullWhen(true, nameof(Reason))]
		public bool Failed => Result == null && Reason != null;

		public T ResultOrThrow => Result ?? throw new NullReferenceException(Reason);

		public EditorReturnResult(T? result, string? reason = null) {
			Result = result;
			Reason = reason;
		}

		public static implicit operator EditorReturnResult<T>(T? result) => result == null ? new(result, "No reason provided.") : new(result);
	}
}
