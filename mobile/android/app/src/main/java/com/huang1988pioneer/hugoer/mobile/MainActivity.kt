package com.huang1988pioneer.hugoer.mobile

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import com.huang1988pioneer.hugoer.mobile.ui.HugoerApp
import com.huang1988pioneer.hugoer.mobile.ui.theme.HugoerTheme

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        setContent {
            HugoerTheme {
                HugoerApp()
            }
        }
    }
}
