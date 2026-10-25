plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
}
android {
    namespace = "com.ipodemu"
    compileSdk = 34
    defaultConfig {
        applicationId = "com.ipodemu"
        minSdk = 30
        targetSdk = 33
        versionCode = 106
        versionName = "1.0-beta91"
        // on-device tests (src/androidTest): a stand-in car browses the playback service the way Android Auto does
        testInstrumentationRunner = "androidx.test.runner.AndroidJUnitRunner"
    }
    buildTypes {
        release {
            isMinifyEnabled = true
            isShrinkResources = true
            proguardFiles(getDefaultProguardFile("proguard-android-optimize.txt"), "proguard-rules.pro")
            // debug key so the release build installs over the debug one on the test device; swap for a real key to publish
            signingConfig = signingConfigs.getByName("debug")
        }
    }
    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    kotlinOptions { jvmTarget = "17" }
    buildFeatures { compose = true }
    composeOptions { kotlinCompilerExtensionVersion = "1.5.14" }
}
// Retro (FLACie's iPods, the desk and the listening scenes: ui/RetroActivity.kt) is FLACie.Retro built for the browser: ipodsync/src/FLACie.RetroWasm,
// published with the .NET SDK and packaged as assets/retro. Rebuilt only when its sources change. -PskipRetro leaves it out (the Retro screen then stays empty).
val ipodsync = rootProject.projectDir.resolve("../ipodsync/src")
val retroPublish = layout.buildDirectory.dir("retro-publish")
val retroAssets = layout.buildDirectory.dir("retro-assets")
val retroWasmPublish by tasks.registering(Exec::class) {
    val sources = listOf("FLACie.RetroWasm", "FLACie.Retro", "FLACie.Core").map { fileTree(ipodsync.resolve(it)) { exclude("bin/**", "obj/**") } } +
        fileTree(ipodsync.resolve("FLACie.Server/wwwroot"))
    inputs.files(sources)
    outputs.dir(retroPublish)
    onlyIf { !project.hasProperty("skipRetro") }
    doFirst { delete(retroPublish) }
    commandLine("dotnet", "publish", ipodsync.resolve("FLACie.RetroWasm/FLACie.RetroWasm.csproj").path, "-c", "Release", "-o", retroPublish.get().asFile.path, "--nologo", "-v", "q")
}
val retroWasm by tasks.registering(Sync::class) {
    dependsOn(retroWasmPublish)
    from(retroPublish.map { it.dir("wwwroot") })
    // the APK leaves out asset folders whose names start with "_": the runtime's goes in as "framework" (RetroFiles maps it back)
    eachFile { path = path.replaceFirst(Regex("^_framework/"), "framework/") }
    includeEmptyDirs = false
    into(retroAssets.map { it.dir("retro") })
}
android.sourceSets.getByName("main").assets.srcDir(retroAssets)
tasks.named("preBuild") { dependsOn(retroWasm) }

dependencies {
    // WebSocket for Jellyfin remote control (Connect) and SyncPlay (Jams)
    implementation("com.squareup.okhttp3:okhttp:4.12.0")
    implementation("androidx.core:core-ktx:1.13.1")
    implementation("androidx.media3:media3-exoplayer:1.4.1")
    // lower-bitrate (HLS) streams of Jellyfin songs for weak connections
    implementation("androidx.media3:media3-exoplayer-hls:1.4.1")
    implementation("androidx.media3:media3-session:1.4.1")
    implementation("org.jetbrains.kotlinx:kotlinx-coroutines-android:1.8.1")
    implementation(platform("androidx.compose:compose-bom:2024.09.03"))
    implementation("androidx.compose.ui:ui")
    implementation("androidx.compose.ui:ui-graphics")
    implementation("androidx.compose.foundation:foundation")
    implementation("androidx.compose.animation:animation")
    implementation("androidx.activity:activity-compose:1.9.0")
    implementation("androidx.lifecycle:lifecycle-runtime-compose:2.8.2")
    implementation("androidx.palette:palette-ktx:1.0.0")
    implementation("androidx.profileinstaller:profileinstaller:1.3.1")
    // SMB/CIFS network-share browsing for the NAS source (pure-Java SMB2/3 client, no native code).
    implementation("eu.agno3.jcifs:jcifs-ng:2.1.10")
    testImplementation("junit:junit:4.13.2")
    testImplementation("org.json:json:20231013")
    testImplementation("org.jetbrains.kotlinx:kotlinx-coroutines-test:1.8.1")
    androidTestImplementation("androidx.test:runner:1.6.2")
    androidTestImplementation("androidx.test.ext:junit:1.2.1")
}
