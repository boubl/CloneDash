﻿using Nucleus.Types;

using Raylib_cs;

using SDL;

using System.Runtime.InteropServices;

namespace Nucleus.Engine;

public static unsafe class OS
{
	private static bool initialized = false;
	public static bool InitSDL() {
		if (initialized) return true;

		if (!SDL3.SDL_Init(
			SDL_InitFlags.SDL_INIT_AUDIO |
			//SDL_InitFlags.SDL_INIT_CAMERA |
			SDL_InitFlags.SDL_INIT_EVENTS |
			SDL_InitFlags.SDL_INIT_GAMEPAD |
			SDL_InitFlags.SDL_INIT_HAPTIC |
			SDL_InitFlags.SDL_INIT_JOYSTICK |
			SDL_InitFlags.SDL_INIT_SENSOR |
			SDL_InitFlags.SDL_INIT_VIDEO
		))
			return false;

		if (!SDL3.SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_STENCIL_SIZE, 8)) return false;
		var version = Rlgl.GetVersion();
		switch (version) {
			case GlVersion.OPENGL_21:
				if (!SDL3.SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_CONTEXT_MAJOR_VERSION, 2)) return false;
				if (!SDL3.SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_CONTEXT_MINOR_VERSION, 1)) return false;
				break;
			case GlVersion.OPENGL_33:
				if (!SDL3.SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_CONTEXT_MAJOR_VERSION, 3)) return false;
				if (!SDL3.SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_CONTEXT_MINOR_VERSION, 3)) return false;
				if (!SDL3.SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_CONTEXT_PROFILE_MASK, (int)SDL_GLProfile.SDL_GL_CONTEXT_PROFILE_CORE)) return false;
				break;
			case GlVersion.OPENGL_43:
				if (!SDL3.SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_CONTEXT_MAJOR_VERSION, 4)) return false;
				if (!SDL3.SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_CONTEXT_MINOR_VERSION, 3)) return false;
				if (!SDL3.SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_CONTEXT_PROFILE_MASK, (int)SDL_GLProfile.SDL_GL_CONTEXT_PROFILE_CORE)) return false;
				break;
			case GlVersion.OPENGL_ES_20:
				if (!SDL3.SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_CONTEXT_MAJOR_VERSION, 2)) return false;
				if (!SDL3.SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_CONTEXT_MINOR_VERSION, 0)) return false;
				if (!SDL3.SDL_GL_SetAttribute(SDL_GLAttr.SDL_GL_CONTEXT_PROFILE_MASK, (int)SDL_GLProfile.SDL_GL_CONTEXT_PROFILE_ES)) return false;
				break;
		}

		initialized = true;
		return true;
	}
	[UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
	public static void* OpenGL_GetProcAddress(byte* name) {
		return (void*)SDL3.SDL_GL_GetProcAddress(name);
	}

	public static bool IsMonitorIDValid(int idx) => idx > 0 && idx < GetMonitorCount();
	public static int GetMonitorCount() => SDL3.SDL_GetDisplays()?.Count ?? 0;
	public static int GetMonitorWidth(int monitor) {
		if (!IsMonitorIDValid(monitor)) { Logs.Warn("(SDL) Failed to find the monitor."); return 0; }
		return SDL3.SDL_GetCurrentDisplayMode((SDL_DisplayID)monitor)->w;
	}
	public static int GetMonitorHeight(int monitor) {
		if (!IsMonitorIDValid(monitor)) { Logs.Warn("(SDL) Failed to find the monitor."); return 0; }
		return SDL3.SDL_GetCurrentDisplayMode((SDL_DisplayID)monitor)->h;
	}
	public static Vector2F GetMonitorPosition(int monitor) {
		if (!IsMonitorIDValid(monitor)) { Logs.Warn("(SDL) Failed to find the monitor."); return new(0); }

		SDL_Rect rect;
		SDL3.SDL_GetDisplayUsableBounds((SDL_DisplayID)monitor, &rect);

		return new(rect.x, rect.y);
	}
	public static Vector2F GetMonitorSize(int monitor) {
		if (!IsMonitorIDValid(monitor)) { Logs.Warn("(SDL) Failed to find the monitor."); return new(0); }

		SDL_Rect rect;
		SDL3.SDL_GetDisplayUsableBounds((SDL_DisplayID)monitor, &rect);

		return new(rect.w, rect.h);
	}

	public static float GetMonitorRefreshRate(int monitor) {
		if (!IsMonitorIDValid(monitor)) { Logs.Warn("(SDL) Failed to find the monitor."); return 0; }
		return SDL3.SDL_GetCurrentDisplayMode((SDL_DisplayID)monitor)->refresh_rate;
	}

	public static string GetDisplayName(int monitor) {
		if (!IsMonitorIDValid(monitor)) { Logs.Warn("(SDL) Failed to find the monitor."); return "<no monitor>"; }
		return SDL3.SDL_GetDisplayName((SDL_DisplayID)monitor) ?? "<null>";
	}

	public static string GetClipboardText() => SDL3.SDL_GetClipboardText() ?? "";
	public static void SetClipboardText(string text) => SDL3.SDL_SetClipboardText(text);

	public static bool HasClipboardText() => SDL3.SDL_HasClipboardText();

	public static void ShowCursor() {
		SDL3.SDL_ShowCursor();
	}

	public static void HideCursor() {
		SDL3.SDL_HideCursor();
	}

	public static double TicksToTime(ulong ticks) => (double)ticks / 1_000_000_000d;
	public static double GetTime() => TicksToTime(SDL3.SDL_GetTicksNS());

	/// <summary>
	/// Performs thread sleeping, but at the end, busy-loops to ensure tight frame timing.
	/// </summary>
	/// <param name="seconds">How long, in seconds, should the thread sleep/busy wait for</param>
	public static void Wait(double seconds) {
		double start = GetTime();
		double sleepFor = seconds - (seconds * 0.05);

		Thread.Sleep((int)(sleepFor * 1000));
		double left = GetTime() - start;
		if (left > 0) {
			while ((GetTime() - start) < seconds) ;
		}
	}
}