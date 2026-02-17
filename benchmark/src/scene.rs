use rand::prelude::*;
use std::ops::{Add, Div, Mul, Sub, Neg};

// --- Vec3 Implementation ---

#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Vec3 {
    pub x: f64,
    pub y: f64,
    pub z: f64,
}

impl Vec3 {
    pub fn new(x: f64, y: f64, z: f64) -> Self {
        Self { x, y, z }
    }

    pub fn length(&self) -> f64 {
        self.length_squared().sqrt()
    }

    pub fn length_squared(&self) -> f64 {
        self.x * self.x + self.y * self.y + self.z * self.z
    }

    pub fn unit_vector(self) -> Self {
        self / self.length()
    }

    pub fn dot(u: &Vec3, v: &Vec3) -> f64 {
        u.x * v.x + u.y * v.y + u.z * v.z
    }

    pub fn random_unit_vector(rng: &mut impl Rng) -> Self {
        loop {
            let p = Vec3::new(rng.random_range(-1.0..1.0), rng.random_range(-1.0..1.0), rng.random_range(-1.0..1.0));
            if p.length_squared() < 1.0 {
                return p.unit_vector();
            }
        }
    }
    pub fn cross(u: &Vec3, v: &Vec3) -> Self {
        Self::new(
            u.y * v.z - u.z * v.y,
            u.z * v.x - u.x * v.z,
            u.x * v.y - u.y * v.x,
        )
    }
}

impl Add for Vec3 {
    type Output = Self;
    fn add(self, other: Self) -> Self {
        Self::new(self.x + other.x, self.y + other.y, self.z + other.z)
    }
}

impl Sub for Vec3 {
    type Output = Self;
    fn sub(self, other: Self) -> Self {
        Self::new(self.x - other.x, self.y - other.y, self.z - other.z)
    }
}

impl Mul<f64> for Vec3 {
    type Output = Self;
    fn mul(self, t: f64) -> Self {
        Self::new(self.x * t, self.y * t, self.z * t)
    }
}

impl Mul<Vec3> for Vec3 {
    type Output = Self;
    fn mul(self, other: Self) -> Self {
        Self::new(self.x * other.x, self.y * other.y, self.z * other.z)
    }
}

impl Div<f64> for Vec3 {
    type Output = Self;
    fn div(self, t: f64) -> Self {
        self * (1.0 / t)
    }
}

impl Neg for Vec3 {
    type Output = Self;
    fn neg(self) -> Self {
        Self::new(-self.x, -self.y, -self.z)
    }
}

// --- Ray Implementation ---

#[derive(Clone, Copy)]
pub struct Ray {
    pub origin: Vec3,
    pub direction: Vec3,
}

impl Ray {
    pub fn new(origin: Vec3, direction: Vec3) -> Self {
        Self { origin, direction }
    }

    pub fn at(&self, t: f64) -> Vec3 {
        self.origin + self.direction * t
    }
}

// --- Hitable & Materials ---

#[derive(Clone, Copy)]
pub enum Material {
    Lambertian { albedo: Vec3 },
    Metal { albedo: Vec3, fuzz: f64 },
}

pub struct HitRecord {
    pub p: Vec3,
    pub normal: Vec3,
    pub t: f64,
    pub material: Material,
}

pub trait Hitable: Sync + Send {
    fn hit(&self, r: &Ray, t_min: f64, t_max: f64) -> Option<HitRecord>;
}

pub struct Sphere {
    pub center: Vec3,
    pub radius: f64,
    pub material: Material,
}

impl Hitable for Sphere {
    fn hit(&self, r: &Ray, t_min: f64, t_max: f64) -> Option<HitRecord> {
        let oc = r.origin - self.center;
        let a = r.direction.length_squared();
        let half_b = Vec3::dot(&oc, &r.direction);
        let c = oc.length_squared() - self.radius * self.radius;
        let discriminant = half_b * half_b - a * c;

        if discriminant < 0.0 {
            return None;
        }

        let sqrtd = discriminant.sqrt();
        let mut root = (-half_b - sqrtd) / a;
        if root < t_min || t_max < root {
            root = (-half_b + sqrtd) / a;
            if root < t_min || t_max < root {
                return None;
            }
        }

        let p = r.at(root);
        let normal = (p - self.center) / self.radius;

        Some(HitRecord {
            t: root,
            p,
            normal,
            material: self.material,
        })
    }
}

// --- Scene ---

pub struct Scene {
    pub objects: Vec<Box<dyn Hitable>>,
}

impl Scene {
    pub fn new() -> Self {
        Self { objects: Vec::new() }
    }

    pub fn add(&mut self, object: Box<dyn Hitable>) {
        self.objects.push(object);
    }

    pub fn hit(&self, r: &Ray, t_min: f64, t_max: f64) -> Option<HitRecord> {
        let mut hit_record = None;
        let mut closest_so_far = t_max;

        for object in &self.objects {
            if let Some(rec) = object.hit(r, t_min, closest_so_far) {
                closest_so_far = rec.t;
                hit_record = Some(rec);
            }
        }

        hit_record
    }

    pub fn random_scene() -> Self {
        let mut scene = Scene::new();
        
        // Ground
        scene.add(Box::new(Sphere {
            center: Vec3::new(0.0, -1000.0, 0.0),
            radius: 1000.0,
            material: Material::Lambertian { albedo: Vec3::new(0.5, 0.5, 0.5) },
        }));

        // Double Helix Structure
        let strands = 2;
        let sphere_count_per_strand = 30; // Number of spheres per helix
        let twist_rate = 0.3; // How fast it twists
        let vertical_step = 0.5; // Vertical distance between spheres
        let radius = 4.0; // Radius of the helix from center
        let sphere_radius = 0.6; 

        for i in 0..sphere_count_per_strand {
            let y = i as f64 * vertical_step + 1.0;
            let angle = i as f64 * twist_rate;

            for strand in 0..strands {
                let strand_offset = strand as f64 * std::f64::consts::PI; // 180 degree offset for 2nd strand
                let final_angle = angle + strand_offset;

                let x = radius * final_angle.cos();
                let z = radius * final_angle.sin();

                // Colorful metallic look
                let albedo = if strand == 0 {
                     // Blue/Cyan strand
                     Vec3::new(0.1, 0.2 + (i as f64 * 0.02) % 0.8, 0.8)
                } else {
                     // Red/Orange strand
                     Vec3::new(0.8, 0.2 + (i as f64 * 0.02) % 0.8, 0.1)
                };

                scene.add(Box::new(Sphere {
                    center: Vec3::new(x, y, z),
                    radius: sphere_radius,
                    material: Material::Metal { albedo, fuzz: 0.1 },
                }));
            }

            // Connecting rungs (base pairs) every few steps
            if i % 2 == 0 {
                let x1 = radius * angle.cos();
                let z1 = radius * angle.sin();
                let x2 = radius * (angle + std::f64::consts::PI).cos();
                let z2 = radius * (angle + std::f64::consts::PI).sin();
                
                // Add a few small spheres to form a "rung"
                let rung_steps = 5;
                for r in 1..rung_steps {
                    let t = r as f64 / rung_steps as f64;
                    let rx = x1 + (x2 - x1) * t;
                    let rz = z1 + (z2 - z1) * t;
                    
                    scene.add(Box::new(Sphere {
                        center: Vec3::new(rx, y, rz),
                        radius: 0.2, // Smaller spheres for rungs
                        material: Material::Lambertian { albedo: Vec3::new(0.8, 0.8, 0.8) },
                    }));
                }
            }
        }
        
        // Add a few random floating spheres for extra reflection context
        let mut rng = rand::rng();
        for _ in 0..10 {
             let x: f64 = rng.random_range(-10.0..10.0);
             let z: f64 = rng.random_range(-10.0..10.0);
             let y: f64 = rng.random_range(0.5..5.0);
             
             // Keep them away from the center helix
             if (x*x + z*z).sqrt() > 6.0_f64 {
                 scene.add(Box::new(Sphere {
                    center: Vec3::new(x, y, z),
                    radius: rng.random_range(0.3..0.8),
                    material: Material::Lambertian { 
                        albedo: Vec3::new(rng.random_range(0.0..1.0), rng.random_range(0.0..1.0), rng.random_range(0.0..1.0)) 
                    },
                }));
             }
        }

        scene
    }
}
