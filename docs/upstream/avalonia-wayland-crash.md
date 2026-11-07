# Relato pronto para o Avalonia — crash intermitente no backend Wayland (rascunho, NÃO enviado)

Para o mantenedor revisar e, se quiser, abrir em https://github.com/AvaloniaUI/Avalonia/issues (em inglês abaixo).
Nada foi publicado pelo time.

---

**Title:** Intermittent NullReferenceException in `WXdgTopLevel.OnConfigureBatchComplete` after windows are closed quickly (Wayland)

**Versions:** Avalonia 12.1.4 (also reproduced, more often, with 12.1.5), .NET 10.0 (SDK 10.0.403), Linux x64.
**System:** Linux Mint 22.3 (Ubuntu 24.04 base), kernel 6.14, Cinnamon session; compositor **Muffin 6.6.1 run nested**
(`muffin --wayland --nested --no-x11`), app started with `DISPLAY` unset and `WAYLAND_DISPLAY` pointing to it.
Hardware: Dell Inspiron 7460 (Intel HD 620 + GeForce 940MX, NVIDIA 580.178.04, PRIME on-demand). Not seen on an AMD Radeon 610M
laptop with the same compositor setup (5+ clean runs).

**What happens:** the process aborts with an unhandled exception on the Wayland worker thread:

```
Unhandled exception. System.NullReferenceException: Object reference not set to an instance of an object.
   at Avalonia.Wayland.Server.Persistent.WXdgTopLevel.OnConfigureBatchComplete(UInt32 serial)
   at Avalonia.Wayland.Server.Persistent.WXdgShellSurface.XdgSurfaceListener.Configure(XdgSurface eventSender, UInt32 serial)
   at NWayland.Protocols.XdgShell.XdgSurface.Listener.global::NWayland.Interop.IWlEventsListener.DispatchEvent(WlEventArgs arguments)
   at NWayland.WlProxy.DispatchEvent(UInt32 opcode, WlMessage& nativeMessage, WlArgument* arguments)
   at NWayland.Interop.LibWayland.WlProxyDispatcher(IntPtr implementation, IntPtr target, UInt32 opcode, WlMessage& message, WlArgument* arguments)
--- End of stack trace from previous location ---
   at NWayland.WlEventQueue.DispatchPending()
   at Avalonia.Wayland.Server.Interop.WaylandConnection.DispatchQueueOrWakeup(WlEventQueue queue, Int32 wakeupFd)
   at Avalonia.Wayland.Server.WaylandWorker.RunConnection(WaylandConnection connection, WaylandPlatformOptions options, TaskCompletionSource`1 initTcs)
   at Avalonia.Wayland.Server.WaylandWorker.Run(WaylandPlatformOptions options, WaylandConnection connection, TaskCompletionSource`1 initTcs)
   at Avalonia.Wayland.Server.WaylandWorker.<>c__DisplayClass36_0.<StartCore>b__0()
   at System.Threading.Thread.StartCallback()
```

**When:** always right after a step of our UI test harness that creates a top-level `Window`, shows it, does a few in-process
interactions and closes it within ~1 s (also modal dialogs opened and closed quickly). It looks like an `xdg_surface.configure`
arriving for a top level that was already closed/disposed.

**Rate observed:** 12.1.4 — 1 crash in 3 full runs (each run opens/closes dozens of windows); 12.1.5 — 3 crashes in 4 runs. X11 backend: 0 in many runs.

**Repro:** seen in our (currently private) app's UI smoke harness, which opens and closes many windows in-process. A minimal repro is
probably: show a `Window`, close it immediately, repeat in a loop with `DISPLAY` unset under a nested compositor. (Before sending: build that
minimal repro and attach it — the maintainer decides what to share.)

**Expected:** a configure event for a closed top level is ignored instead of crashing the process.
