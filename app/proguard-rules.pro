# Media3 / Compose / AndroidX ship consumer rules. App-specific keeps:
-keepattributes SourceFile,LineNumberTable
# The classic wheel UI is a plain View created from Compose's AndroidView; nothing is reflective, but keep the
# activity/service entry points explicit.
-keep class com.ipodemu.ui.MainActivity { *; }
-keep class com.ipodemu.playback.PlaybackService { *; }

# jcifs-ng (NAS/SMB): SLF4J's optional static binder is absent at runtime (falls back to NOP), and jcifs loads its
# protocol/auth classes reflectively, so keep it whole.
-dontwarn org.slf4j.impl.StaticLoggerBinder
-keep class jcifs.** { *; }
-dontwarn jcifs.**
# ...and its crypto (NTLM MD4/HMAC) comes from BouncyCastle, looked up through JCA providers by name.
-keep class org.bouncycastle.** { *; }
-dontwarn org.bouncycastle.**
