use serde::{Deserialize, Serialize};

#[allow(dead_code)]
#[derive(Serialize, Deserialize)]
pub struct Ranking {
    pub name: String,
    pub score: f64,
}
