using System.Runtime.InteropServices;
static class Egl {
[DllImport("libEGL.so.1")] public static extern IntPtr eglGetPlatformDisplay(uint platform,IntPtr display,IntPtr attrs);
[DllImport("libEGL.so.1")] public static extern int eglInitialize(IntPtr d,out int major,out int minor);
[DllImport("libEGL.so.1")] public static extern int eglBindAPI(uint api);
[DllImport("libEGL.so.1")] public static extern int eglChooseConfig(IntPtr d,int[] a,out IntPtr config,int size,out int count);
[DllImport("libEGL.so.1")] public static extern IntPtr eglCreatePbufferSurface(IntPtr d,IntPtr config,int[] a);
[DllImport("libEGL.so.1")] public static extern IntPtr eglCreateContext(IntPtr d,IntPtr config,IntPtr share,int[] a);
[DllImport("libEGL.so.1")] public static extern int eglMakeCurrent(IntPtr d,IntPtr draw,IntPtr read,IntPtr ctx);
[DllImport("libEGL.so.1")] public static extern int eglGetError();
[DllImport("libEGL.so.1")] public static extern int eglDestroyContext(IntPtr d,IntPtr ctx);
[DllImport("libEGL.so.1")] public static extern int eglDestroySurface(IntPtr d,IntPtr s);
[DllImport("libEGL.so.1")] public static extern int eglTerminate(IntPtr d);
}
