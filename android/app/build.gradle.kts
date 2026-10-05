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
        versionCode = 33
        versionName = "1.0-beta19"
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
dependencies {
    // WebSocket for Jellyfin remote control (Connect) and SyncPlay (Jams)
    implementation("com.squareup.okhttp3:okhttp:4.12.0")
    implementation("androidx.core:core-ktx:1.13.1")
    implementation("androidx.media3:media3-exoplayer:1.4.1")
    // lower-bitrate (HLS) streams of Jellyfin songs for weak connections
    implementation("androidx.media3:media3-exoplayer-hls:1.4.1")
    implementation("androidx.media3:media3-session:1.4.1")
    implementation("org.jetbrains.kotlinx:kotlinx-coroutines-android:1.8.1")
    implementation(platform("androidx.compose:compose-bom:2024.06.00"))
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
}
