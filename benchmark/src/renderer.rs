use crate::scene::{Material, Ray, Scene, Vec3};

use rayon::prelude::*;
use std::time::Instant;
use rand::Rng;
use rand::seq::SliceRandom;
use std::sync::mpsc::Sender;

const MAX_DEPTH: i32 = 10;
const SAMPLES_PER_PIXEL: i32 = 10; // Fast benchmark
const IMAGE_WIDTH: u32 = 800;   // Fixed width for consistent scoring
const IMAGE_HEIGHT: u32 = 600;

fn ray_color(r: &Ray, scene: &Scene, depth: i32) -> Vec3 {
    if depth <= 0 {
        return Vec3::new(0.0, 0.0, 0.0);
    }

    if let Some(rec) = scene.hit(r, 0.001, f64::INFINITY) {
        match rec.material {
            Material::Lambertian { albedo } => {
                let target = rec.p + rec.normal + Vec3::random_unit_vector(&mut rand::rng());
                ray_color(&Ray::new(rec.p, target - rec.p), scene, depth - 1) * 0.5 * albedo
            }
            Material::Metal { albedo, fuzz } => {
                let reflected = reflect(r.direction.unit_vector(), rec.normal);
                let scattered = Ray::new(rec.p, reflected + Vec3::random_unit_vector(&mut rand::rng()) * fuzz);
                if Vec3::dot(&scattered.direction, &rec.normal) > 0.0 {
                    ray_color(&scattered, scene, depth - 1) * albedo
                } else {
                    Vec3::new(0.0, 0.0, 0.0)
                }
            }
        }
    } else {
        let unit_direction = r.direction.unit_vector();
        let t = 0.5 * (unit_direction.y + 1.0);
        Vec3::new(1.0, 1.0, 1.0) * (1.0 - t) + Vec3::new(0.5, 0.7, 1.0) * t
    }
}

fn reflect(v: Vec3, n: Vec3) -> Vec3 {
    v - n * 2.0 * Vec3::dot(&v, &n)
}

pub enum RenderMessage {
    Tile { x: u32, y: u32, width: u32, height: u32, data: Vec<u8> },
    Done(std::time::Duration),
}

const TILE_SIZE: u32 = 32;

pub fn render(tx: Sender<RenderMessage>, scene: &Scene) {
    let start_time = Instant::now();

    // Camera Setup
    let lookfrom = Vec3::new(13.0, 2.0, 3.0);
    let lookat = Vec3::new(0.0, 0.0, 0.0);
    let vup = Vec3::new(0.0, 1.0, 0.0);
    let dist_to_focus = 10.0;
    let vfov: f64 = 20.0;

    let theta = vfov.to_radians();
    let h = (theta / 2.0).tan();
    let aspect_ratio = IMAGE_WIDTH as f64 / IMAGE_HEIGHT as f64;
    let viewport_height = 2.0 * h;
    let viewport_width = aspect_ratio * viewport_height;

    let w = (lookfrom - lookat).unit_vector();
    let u = Vec3::cross(&vup, &w).unit_vector();
    let v = Vec3::cross(&w, &u);

    let origin = lookfrom;
    let horizontal = u * viewport_width * dist_to_focus;
    let vertical = v * viewport_height * dist_to_focus;
    let lower_left_corner = origin - horizontal / 2.0 - vertical / 2.0 - w * dist_to_focus;

    // Generate tiles
    let mut tiles = Vec::new();
    for y in (0..IMAGE_HEIGHT).step_by(TILE_SIZE as usize) {
        for x in (0..IMAGE_WIDTH).step_by(TILE_SIZE as usize) {
            tiles.push((x, y));
        }
    }

    // Shuffle tiles for random rendering order
    let mut rng = rand::rng();
    tiles.shuffle(&mut rng);

    // Parallelize over tiles
    tiles.into_par_iter().for_each_with(tx.clone(), |s, (tile_x, tile_y)| {
        let tile_w = std::cmp::min(TILE_SIZE, IMAGE_WIDTH - tile_x);
        let tile_h = std::cmp::min(TILE_SIZE, IMAGE_HEIGHT - tile_y);
        let mut tile_data = Vec::with_capacity((tile_w * tile_h * 3) as usize);
        let mut rng = rand::rng();

        for y_offset in 0..tile_h {
            let y_coord = tile_y + y_offset;
            // Map y to viewport coordinates (inverted y)
            let y_inverted = IMAGE_HEIGHT - 1 - y_coord;
            
            for x_offset in 0..tile_w {
                let x_coord = tile_x + x_offset;
                
                let mut pixel_color = Vec3::new(0.0, 0.0, 0.0);
                for _ in 0..SAMPLES_PER_PIXEL {
                    let u = (x_coord as f64 + rng.random::<f64>()) / (IMAGE_WIDTH - 1) as f64;
                    let v = (y_inverted as f64 + rng.random::<f64>()) / (IMAGE_HEIGHT - 1) as f64;

                    let r = Ray::new(origin, lower_left_corner + horizontal * u + vertical * v - origin);
                    pixel_color = pixel_color + ray_color(&r, scene, MAX_DEPTH);
                }
                
                // sqrt for gamma 2.0
                let scale = 1.0 / SAMPLES_PER_PIXEL as f64;
                let r = (pixel_color.x * scale).sqrt();
                let g = (pixel_color.y * scale).sqrt();
                let b = (pixel_color.z * scale).sqrt();

                tile_data.push((256.0 * r.clamp(0.0, 0.999)) as u8);
                tile_data.push((256.0 * g.clamp(0.0, 0.999)) as u8);
                tile_data.push((256.0 * b.clamp(0.0, 0.999)) as u8);
            }
        }

        // Send tile update
        let _ = s.send(RenderMessage::Tile {
            x: tile_x,
            y: tile_y,
            width: tile_w,
            height: tile_h,
            data: tile_data
        });
    });

    let duration = start_time.elapsed();
    let _ = tx.send(RenderMessage::Done(duration));
}
