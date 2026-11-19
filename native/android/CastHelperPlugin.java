package com.marvelous.railrush;

import android.content.Intent;
import android.provider.Settings;
import com.getcapacitor.Plugin;
import com.getcapacitor.PluginCall;
import com.getcapacitor.PluginMethod;
import com.getcapacitor.annotation.CapacitorPlugin;

@CapacitorPlugin(name = "CastHelper")
public class CastHelperPlugin extends Plugin {
  @PluginMethod
  public void open(PluginCall call) {
    String[] actions = {"android.settings.CAST_SETTINGS", "android.settings.WIFI_DISPLAY_SETTINGS", Settings.ACTION_WIRELESS_SETTINGS};
    for (String a : actions) {
      try { Intent i = new Intent(a); i.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK); getActivity().startActivity(i); call.resolve(); return; }
      catch (Exception ignored) {}
    }
    call.reject("No cast settings on this device");
  }
}
