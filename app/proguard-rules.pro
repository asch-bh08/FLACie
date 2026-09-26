# Media3 / Compose / AndroidX ship consumer rules. App-specific keeps:
-keepattributes SourceFile,LineNumberTable
# The classic wheel UI is a plain View created from Compose's AndroidView; nothing is reflective, but keep the
# activity/service entry points explicit.
-keep class com.ipodemu.ui.MainActivity { *; }
-keep class com.ipodemu.playback.PlaybackService { *; }
