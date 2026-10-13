plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
}
// A tiny separate app (another package, another uid) that browses and plays through FLACie's media library service the way Android Auto or a head unit does.
// A test tool, never shipped: build with  gradlew :autosim:assembleDebug  and run with  am start -n com.ipodemu.autosim/.SimActivity  (log tag FLACieAutoSim).
android {
    namespace = "com.ipodemu.autosim"
    compileSdk = 34
    defaultConfig { applicationId = "com.ipodemu.autosim"; minSdk = 30; targetSdk = 33; versionCode = 1; versionName = "1" }
    compileOptions { sourceCompatibility = JavaVersion.VERSION_17; targetCompatibility = JavaVersion.VERSION_17 }
    kotlinOptions { jvmTarget = "17" }
}
dependencies {
    implementation("androidx.core:core-ktx:1.13.1")
    implementation("androidx.media3:media3-session:1.4.1")
    implementation("androidx.media3:media3-common:1.4.1")
}
