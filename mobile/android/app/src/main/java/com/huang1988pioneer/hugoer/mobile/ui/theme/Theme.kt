package com.huang1988pioneer.hugoer.mobile.ui.theme

import android.os.Build
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.dp

private val Graphite = Color(0xFF101418)
private val GraphiteElevated = Color(0xFF1A2229)
private val Paper = Color(0xFFF7FAFB)
private val Ink = Color(0xFF172027)
private val HugoCyan = Color(0xFF8DE5F5)
private val HugoCyanDark = Color(0xFF006878)
private val Amber = Color(0xFFFFC36B)
private val AmberDark = Color(0xFF825500)
private val MutedCyan = Color(0xFFB6EAF1)

private val DarkColors = darkColorScheme(
    primary = HugoCyan,
    onPrimary = Color(0xFF00363D),
    primaryContainer = Color(0xFF004F5B),
    onPrimaryContainer = Color(0xFFB6F2FB),
    secondary = Amber,
    onSecondary = Color(0xFF452B00),
    secondaryContainer = Color(0xFF604000),
    onSecondaryContainer = Color(0xFFFFDDA8),
    tertiary = MutedCyan,
    background = Graphite,
    onBackground = Color(0xFFE3EDF0),
    surface = Graphite,
    surfaceContainer = GraphiteElevated,
    surfaceVariant = Color(0xFF243139),
    onSurface = Color(0xFFE3EDF0),
    onSurfaceVariant = Color(0xFFB9C8CC),
    outline = Color(0xFF849499),
    error = Color(0xFFFFB4AB),
)

private val LightColors = lightColorScheme(
    primary = HugoCyanDark,
    onPrimary = Color.White,
    primaryContainer = Color(0xFFB6F2FB),
    onPrimaryContainer = Color(0xFF001F24),
    secondary = AmberDark,
    onSecondary = Color.White,
    secondaryContainer = Color(0xFFFFDDA8),
    onSecondaryContainer = Color(0xFF2A1800),
    tertiary = Color(0xFF3D656B),
    background = Paper,
    onBackground = Ink,
    surface = Paper,
    surfaceContainer = Color(0xFFEFF4F5),
    surfaceVariant = Color(0xFFDCE8EA),
    onSurface = Ink,
    onSurfaceVariant = Color(0xFF3F4B4E),
    outline = Color(0xFF6E7A7E),
)

@Composable
fun HugoerTheme(
    darkTheme: Boolean = isSystemInDarkTheme(),
    dynamicColor: Boolean = true,
    content: @Composable () -> Unit,
) {
    val colors = when {
        dynamicColor && Build.VERSION.SDK_INT >= Build.VERSION_CODES.S -> {
            // The Hugoer cyan/amber roles remain the brand fallback. Dynamic Color can
            // be enabled by the host later without changing screen-level semantics.
            if (darkTheme) DarkColors else LightColors
        }
        darkTheme -> DarkColors
        else -> LightColors
    }

    MaterialTheme(
        colorScheme = colors,
        typography = HugoerTypography,
        shapes = HugoerShapes,
        content = content,
    )
}

private val HugoerTypography = androidx.compose.material3.Typography()

private val HugoerShapes = androidx.compose.material3.Shapes(
    extraSmall = androidx.compose.foundation.shape.RoundedCornerShape(8.dp),
    small = androidx.compose.foundation.shape.RoundedCornerShape(12.dp),
    medium = androidx.compose.foundation.shape.RoundedCornerShape(16.dp),
    large = androidx.compose.foundation.shape.RoundedCornerShape(24.dp),
    extraLarge = androidx.compose.foundation.shape.RoundedCornerShape(28.dp),
)
