use eframe::egui;
use std::sync::mpsc::{channel, Receiver, Sender};
use std::thread;

mod renderer;
mod scene;
mod ranking;

use renderer::{render, RenderMessage};
use scene::Scene;

fn main() -> Result<(), eframe::Error> {
    let options = eframe::NativeOptions {
        viewport: egui::ViewportBuilder::default()
            .with_inner_size([800.0, 600.0]),
        ..Default::default()
    };
    eframe::run_native(
        "HotCPU Benchmark",
        options,
        Box::new(|_cc| Box::new(HotCpuApp::default())),
    )
}

use sysinfo::System;
use std::time::Instant;

struct HotCpuApp {
    is_running: bool,
    rx: Option<Receiver<RenderMessage>>,
    pixels_processed: usize,
    image_texture: Option<egui::TextureHandle>,
    racer_texture: Option<egui::TextureHandle>,
    displayed_image: egui::ColorImage,
    start_time: Option<Instant>,
    final_duration: Option<std::time::Duration>,
    cpu_name: String,
}

impl Default for HotCpuApp {
    fn default() -> Self {
        let mut sys = System::new_all();
        sys.refresh_cpu();
        let cpu_name = sys.cpus().first().map(|cpu| cpu.brand().to_string()).unwrap_or_else(|| "Unknown CPU".to_string());

        Self {
            is_running: false,
            rx: None,
            image_texture: None,
            racer_texture: None,
            displayed_image: egui::ColorImage::new([800, 600], egui::Color32::BLACK),
            start_time: None,
            final_duration: None,
            pixels_processed: 0,
            cpu_name,
        }
    }
}

impl eframe::App for HotCpuApp {
    fn update(&mut self, ctx: &egui::Context, _frame: &mut eframe::Frame) {
        // ... (existing update logic) ...
        // Check for results from the background thread
        if let Some(rx) = &self.rx {
            let mut updated = false;
            // Process all available messages
            while let Ok(msg) = rx.try_recv() {
                match msg {
                    RenderMessage::Tile { x, y, width, height, data } => {
                        let img_width = self.displayed_image.width();
                        let img_height = self.displayed_image.height();
                        
                        self.pixels_processed += (width * height) as usize;

                        for dy in 0..height as usize {
                            let curr_y = y as usize + dy;
                            if curr_y >= img_height { break; }
                            
                            for dx in 0..width as usize {
                                let curr_x = x as usize + dx;
                                if curr_x >= img_width { break; }

                                let src_offset = (dy * width as usize + dx) * 3;
                                let r = data[src_offset];
                                let g = data[src_offset + 1];
                                let b = data[src_offset + 2];
                                
                                let dest_idx = curr_y * img_width + curr_x;
                                self.displayed_image.pixels[dest_idx] = egui::Color32::from_rgb(r, g, b);
                            }
                        }
                        updated = true;
                    }
                    RenderMessage::Done(duration) => {
                        self.is_running = false;
                        self.rx = None; // Stop listening
                        self.final_duration = Some(duration);
                        self.pixels_processed = 800 * 600; // Ensure 100%
                        updated = true;
                        break; // Stop receiving for this frame if done
                    }
                }
            }

            if updated || self.is_running {
                 // Load/Update image into texture
                 self.image_texture = Some(ctx.load_texture(
                    "render_result",
                    self.displayed_image.clone(),
                    Default::default()
                ));
                ctx.request_repaint();
            }
        }

        egui::CentralPanel::default().show(ctx, |ui| {
            ui.heading("HotCPU Benchmark - Race Mode");
            ui.heading(format!("CPU: {}", self.cpu_name)); // Display CPU Name
            
            // --- Race Track UI ---
            let total_pixels = 800.0 * 600.0;
            let progress = (self.pixels_processed as f32 / total_pixels as f32).clamp(0.0, 1.0);
            
            ui.add_space(10.0);
            
            let (rect, _response) = ui.allocate_exact_size(egui::vec2(ui.available_width(), 60.0), egui::Sense::hover());
            let painter = ui.painter();
            
            // Draw Track Background
            let track_rect = rect.shrink2(egui::vec2(0.0, 20.0));
            painter.rect_filled(track_rect, 5.0, egui::Color32::from_gray(40));
            
            // Draw Finish Line
            let finish_line_x = track_rect.right() - 20.0;
            painter.line_segment(
                [egui::pos2(finish_line_x, track_rect.top()), egui::pos2(finish_line_x, track_rect.bottom())], 
                egui::Stroke::new(4.0, egui::Color32::WHITE)
            );
            painter.text(
                egui::pos2(finish_line_x, track_rect.bottom() + 5.0),
                egui::Align2::CENTER_TOP,
                "FINISH",
                egui::FontId::proportional(12.0),
                egui::Color32::WHITE,
            );

            // Draw Car/Marker
            // Calculate X pos based on progress. Start at left, end at finish line.
            let start_x = track_rect.left() + 10.0;
            let current_x = start_x + (finish_line_x - start_x) * progress;
            
            // Lazy load racer texture
            if self.racer_texture.is_none() {
                let icon_data = include_bytes!("../../Images/AppIcon.ico");
                if let Ok(image) = image::load_from_memory(icon_data) {
                    let size = [image.width() as usize, image.height() as usize];
                    let image_buffer = image.to_rgba8();
                    let pixels = image_buffer.as_flat_samples();
                    let color_image = egui::ColorImage::from_rgba_unmultiplied(
                        size,
                        pixels.as_slice(),
                    );
                    self.racer_texture = Some(ctx.load_texture(
                        "racer_icon",
                        color_image,
                        Default::default()
                    ));
                }
            }

            if let Some(texture) = &self.racer_texture {
                let icon_size = 24.0;
                let half_size = icon_size / 2.0;
                let rect = egui::Rect::from_min_size(
                    egui::pos2(current_x - half_size, track_rect.center().y - half_size),
                    egui::vec2(icon_size, icon_size)
                );
                painter.image(
                    texture.id(),
                    rect,
                    egui::Rect::from_min_max(egui::pos2(0.0, 0.0), egui::pos2(1.0, 1.0)),
                    egui::Color32::WHITE
                );
            } else {
                // Fallback if loading fails
                 painter.text(
                    egui::pos2(current_x, track_rect.center().y),
                    egui::Align2::CENTER_CENTER,
                    "🏎️", 
                    egui::FontId::proportional(20.0),
                    egui::Color32::WHITE,
                );
            }
            
            // Draw Timer above Finish Line (Static)
            if let Some(start) = self.start_time {
                let elapsed = if self.is_running { start.elapsed() } else { self.final_duration.unwrap_or(std::time::Duration::ZERO) };
                let time_text = format!("{:.0} ms", elapsed.as_secs_f64() * 1000.0);
                
                painter.text(
                    egui::pos2(finish_line_x, track_rect.top() - 15.0),
                    egui::Align2::RIGHT_BOTTOM,
                    time_text,
                    egui::FontId::monospace(14.0),
                    egui::Color32::YELLOW,
                );
            }

            ui.add_space(20.0);

            if self.is_running {
                ui.label("Rendering...");
                if self.image_texture.is_none() {
                     ui.spinner();
                }
            } else {
                let btn_size = egui::vec2(200.0, 50.0);
                ui.vertical_centered(|ui| {
                    if ui.add(egui::Button::new(egui::RichText::new("START RACE").size(24.0).strong())
                        .min_size(btn_size))
                        .clicked() 
                    {
                        self.is_running = true;
                        self.final_duration = None;
                        self.start_time = Some(Instant::now());
                        self.pixels_processed = 0;
                        
                        // Reset image to black
                        self.displayed_image = egui::ColorImage::new([800, 600], egui::Color32::BLACK);
                        self.image_texture = Some(ctx.load_texture(
                            "render_result",
                            self.displayed_image.clone(),
                            Default::default()
                        ));
    
                        let (tx, rx): (Sender<RenderMessage>, Receiver<RenderMessage>) = channel();
                        self.rx = Some(rx);
                        
                        let ctx_clone = ctx.clone();
    
                        thread::spawn(move || {
                            let scene = Scene::random_scene();
                            render(tx, &scene);
                            ctx_clone.request_repaint();
                        });
                    }
                });
            }

            ui.add_space(10.0);
            
            if let Some(texture) = &self.image_texture {
                ui.image(texture);
            }
        });
    }
}
